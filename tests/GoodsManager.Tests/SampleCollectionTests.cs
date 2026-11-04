using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoodsManager.Web.Collections;
using GoodsManager.Web.Images;
using GoodsManager.Web.Products;
using GoodsManager.Web.Samples;
using GoodsManager.Web.Search;
using GoodsManager.Web.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using static GoodsManager.Tests.AuthenticationTests;

namespace GoodsManager.Tests;

/// <summary>初期登録から独立したサンプル準備と、障害時のロールバック・再実行を検証する。</summary>
public sealed class SampleCollectionTests
{
    /// <summary>ワーカーの自動実行だけを止め、試験から同じ実処理を決めた時点で実行する。</summary>
    private static TestApplication App(Action<IServiceCollection>? configure = null) => new()
    {
        ConfigureTestServices = services =>
        {
            var worker = services.Single(x => x.ServiceType == typeof(IHostedService) && x.ImplementationType == typeof(SampleCollectionWorker));
            services.Remove(worker);
            configure?.Invoke(services);
        }
    };

    /// <summary>専用ワーカーを通常の依存関係で作り、処理を完了させる。</summary>
    private static Task PrepareAsync(TestApplication app, Guid id) => ActivatorUtilities.CreateInstance<SampleCollectionWorker>(app.Services).ProcessAsync(id, CancellationToken.None);

    /// <summary>CSRFと再送キーを通して管理APIを呼び、応答の保存を確認する。</summary>
    private static async Task<HttpResponseMessage> WriteAsync(HttpClient client, string path, object input, HttpMethod? method = null, string? etag = null, Guid? key = null)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
        using var request = new HttpRequestMessage(method ?? HttpMethod.Post, "/api/v1/" + path) { Content = JsonContent.Create(input) };
        request.Headers.Add("X-CSRF-Token", csrf.GetProperty("token").GetString());
        request.Headers.Add("Idempotency-Key", (key ?? Guid.NewGuid()).ToString("D"));
        if (etag is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", etag);
        }

        return await client.SendAsync(request);
    }

    /// <summary>実APIで専用コレクションを作り、準備前の応答を読み取る。</summary>
    private static async Task<CollectionSpaceResult> CreateAsync(HttpClient client, bool samples = false)
    {
        using var response = await WriteAsync(client, "collections", new
        {
            name = "試用",
            withSamples = samples
        });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created), await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<CollectionSpaceResult>() ?? throw new InvalidOperationException("作成結果がない");
    }

    /// <summary>画面が参照する作成結果の必要部分。</summary>
    private sealed record CollectionSpaceResult(Guid Id, string Name, string Etag, string? SampleStatus);

    /// <summary>チェックなしの初期登録へ余分なサンプルを作らず、選択時も空のマイコレクションを保持する。</summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task SetupCreatesOptionalIndependentSamples(bool samples)
    {
        await using var app = App();
        using var client = Client(app);
        using var setup = await SendAsync(client, "setup", new
        {
            loginId = "owner",
            password = Password,
            setupToken = File.ReadLines(Path.Combine(app.DataDirectory, "setup-token.txt")).First(),
            createSampleCollection = samples
        });
        Assert.That(setup.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var spaces = await client.GetFromJsonAsync<JsonElement>("/api/v1/collections");
        Assert.That(spaces.GetProperty("items").GetArrayLength(), Is.EqualTo(samples ? 2 : 1));
        if (!samples)
        {
            return;
        }

        var sample = spaces.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("sampleStatus").GetString() == "pending");
        var id = sample.GetProperty("id").GetGuid();
        client.DefaultRequestHeaders.Remove("X-Collection-Id");
        client.DefaultRequestHeaders.Add("X-Collection-Id", id.ToString("D"));
        using var earlyWrite = await WriteAsync(client, "masters/tags", new
        {
            name = "準備中は追加しない"
        });
        Assert.That(earlyWrite.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        await PrepareAsync(app, id);
        await PrepareAsync(app, id);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.CollectionId = id;
        Assert.That(await db.Set<Product>().CountAsync(), Is.EqualTo(64));
        Assert.That(await db.Set<Product>().CountAsync(x => x.DeletedAt != null), Is.EqualTo(2));
        Assert.That(await db.Set<InventoryItem>().CountAsync(), Is.EqualTo(10));
        Assert.That(await db.Set<ProductSearchDocument>().CountAsync(), Is.EqualTo(64));
        var photo = await db.Set<ProductImage>().FirstAsync();
        Assert.That((await client.GetAsync($"/media/assets/{photo.ImageAssetId}/thumbnail?collectionId={id}")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        db.CollectionId = CollectionSchema.DefaultId;
        Assert.That(await db.Set<Product>().CountAsync(), Is.Zero);
        Assert.That(db.Database.HasPendingModelChanges(), Is.False);
    }

    /// <summary>失敗時に商品を残さず、アカウントを保ったまま再実行できる。</summary>
    [Test]
    public async Task FailedPreparationRollsBackAndCanBeRetried()
    {
        SwitchableFiles? faulty = null;
        await using var app = App(services => services.Replace(ServiceDescriptor.Singleton<IImageFiles>(provider => faulty = new SwitchableFiles(new ImageFiles(provider.GetRequiredService<DatabaseRuntime>())))));
        using var client = Client(app);
        await SetupAsync(client);
        var space = await CreateAsync(client, true);
        await PrepareAsync(app, space.Id);

        var spaces = await client.GetFromJsonAsync<JsonElement>("/api/v1/collections");
        var failed = spaces.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == space.Id);
        Assert.That(failed.GetProperty("sampleStatus").GetString(), Is.EqualTo("failed"));
        Assert.That((await client.GetAsync("/api/v1/auth/session")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.CollectionId = space.Id;
            Assert.That(await db.Set<Product>().CountAsync(), Is.Zero);
            Assert.That(await db.Set<GoodsManager.Web.Masters.Work>().CountAsync(), Is.Zero);
        }

        Assert.That(faulty, Is.Not.Null);
        if (faulty is null)
        {
            throw new InvalidOperationException("故障用サービスが作成されていない");
        }

        faulty.Fail = false;
        using var retry = await WriteAsync(client, $"collections/{space.Id}/samples/retry", new
        {
        }, etag: failed.GetProperty("etag").GetString());
        Assert.That(retry.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        await PrepareAsync(app, space.Id);
        await using var final = app.Services.CreateAsyncScope();
        var restored = final.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.That(await restored.Set<Product>().CountAsync(), Is.EqualTo(64));
    }

    /// <summary>画像書込の故障だけを再現し、復旧後は同じ実装へ委譲する。</summary>
    private sealed class SwitchableFiles(IImageFiles inner) : IImageFiles
    {
        /// <summary>再実行時に保存先が復旧した状態へ切り替える。</summary>
        public bool Fail { get; set; } = true;
        /// <inheritdoc />
        public string Root => inner.Root;
        /// <inheritdoc />
        public string NewStagingPath() => Fail ? throw new IOException("試験用の保存失敗") : inner.NewStagingPath();
        /// <inheritdoc />
        public string NewJobDirectory() => inner.NewJobDirectory();
        /// <inheritdoc />
        public string Resolve(string path) => inner.Resolve(path);
        /// <inheritdoc />
        public Task<string> HashAsync(string path, CancellationToken token) => inner.HashAsync(path, token);
        /// <inheritdoc />
        public Task PublishAsync(string path, string hash, CancellationToken token) => inner.PublishAsync(path, hash, token);
        /// <inheritdoc />
        public Task VerifyAsync(ImageFile file, CancellationToken token) => inner.VerifyAsync(file, token);
        /// <inheritdoc />
        public void Delete(string path) => inner.Delete(path);
    }
}
