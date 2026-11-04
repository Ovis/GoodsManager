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

/// <summary>初期登録から独立したサンプル準備と、専用コレクションの削除・再送・最後の所属保護を検証する。</summary>
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

    /// <summary>確認後の変更を拒否し、全体削除後も同じ要求に同じ結果を返す。共有ファイルは別コレクションへ残す。</summary>
    [Test]
    public async Task DeleteChecksImpactAndReplaysAfterRemovingAllScopedRows()
    {
        await using var app = App();
        using var client = Client(app);
        await SetupAsync(client);
        var first = await CreateAsync(client, true);
        await PrepareAsync(app, first.Id);
        var second = await CreateAsync(client, true);
        await PrepareAsync(app, second.Id);
        var path = $"collections/{first.Id}";
        var impact = await client.GetFromJsonAsync<CollectionDeleteImpact>("/api/v1/" + path + "/delete-impact") ?? throw new InvalidOperationException();
        Assert.That(impact.Products, Is.EqualTo(62));
        Assert.That(impact.TrashedProducts, Is.EqualTo(2));
        Assert.That(impact.CanDelete, Is.True);

        await CreateAsync(client);
        using var stale = await WriteAsync(client, path, new
        {
            name = impact.Name,
            confirmation = impact.Confirmation
        }, HttpMethod.Delete, impact.Etag);
        Assert.That(stale.StatusCode, Is.EqualTo(HttpStatusCode.PreconditionFailed));
        impact = await client.GetFromJsonAsync<CollectionDeleteImpact>("/api/v1/" + path + "/delete-impact") ?? throw new InvalidOperationException();
        var key = Guid.NewGuid();
        var input = new
        {
            name = impact.Name,
            confirmation = impact.Confirmation
        };
        using var deleted = await WriteAsync(client, path, input, HttpMethod.Delete, impact.Etag, key);
        Assert.That(deleted.StatusCode, Is.EqualTo(HttpStatusCode.OK), await deleted.Content.ReadAsStringAsync());
        using var replay = await WriteAsync(client, path, input, HttpMethod.Delete, impact.Etag, key);
        Assert.That(await replay.Content.ReadAsStringAsync(), Is.EqualTo(await deleted.Content.ReadAsStringAsync()));
        Assert.That(replay.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        using var changedReplay = await WriteAsync(client, path, new
        {
            name = "別の確認",
            confirmation = impact.Confirmation
        }, HttpMethod.Delete, impact.Etag, key);
        Assert.That(changedReplay.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.CollectionId = first.Id;
        foreach (var type in CollectionSchema.ScopedTypes)
        {
            var table = db.Model.FindEntityType(type)?.GetTableName() ?? throw new InvalidOperationException();
            using var command = db.Database.GetDbConnection().CreateCommand();
            await db.Database.OpenConnectionAsync();
            command.CommandText = $"SELECT count(*) FROM \"{table}\" WHERE CollectionId=$id";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "$id";
            parameter.Value = first.Id.ToString("D");
            command.Parameters.Add(parameter);
            Assert.That(Convert.ToInt32(await command.ExecuteScalarAsync()), Is.Zero, type.Name);
        }

        db.CollectionId = second.Id;
        Assert.That(await db.Set<Product>().CountAsync(), Is.EqualTo(64));
        var photo = await db.Set<ProductImage>().FirstAsync();
        client.DefaultRequestHeaders.Remove("X-Collection-Id");
        client.DefaultRequestHeaders.Add("X-Collection-Id", second.Id.ToString("D"));
        Assert.That((await client.GetAsync($"/media/assets/{photo.ImageAssetId}/display?collectionId={second.Id}")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    /// <summary>最初の所属も別の所属があれば削除できるが、競合する2件の削除で0件にしない。</summary>
    [Test]
    public async Task DefaultCanBeDeletedButConcurrentDeletesKeepOneCollection()
    {
        await using var app = App();
        using var client = Client(app);
        await SetupAsync(client);
        var second = await CreateAsync(client);
        var firstImpact = await client.GetFromJsonAsync<CollectionDeleteImpact>($"/api/v1/collections/{CollectionSchema.DefaultId}/delete-impact") ?? throw new InvalidOperationException();
        using var defaultDeleted = await WriteAsync(client, $"collections/{firstImpact.Id}", new
        {
            name = firstImpact.Name,
            confirmation = firstImpact.Confirmation
        }, HttpMethod.Delete, firstImpact.Etag);
        Assert.That(defaultDeleted.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var third = await CreateAsync(client);
        firstImpact = await client.GetFromJsonAsync<CollectionDeleteImpact>($"/api/v1/collections/{third.Id}/delete-impact") ?? throw new InvalidOperationException();
        var secondImpact = await client.GetFromJsonAsync<CollectionDeleteImpact>($"/api/v1/collections/{second.Id}/delete-impact") ?? throw new InvalidOperationException();
        var results = await Task.WhenAll(new[] { firstImpact, secondImpact }.Select(impact => WriteAsync(client, $"collections/{impact.Id}", new { name = impact.Name, confirmation = impact.Confirmation }, HttpMethod.Delete, impact.Etag)));
        Assert.That(results.Count(x => x.StatusCode == HttpStatusCode.OK), Is.EqualTo(1));
        Assert.That(results.Count(x => x.StatusCode == HttpStatusCode.Conflict), Is.EqualTo(1));
        var remaining = await client.GetFromJsonAsync<JsonElement>("/api/v1/collections");
        Assert.That(remaining.GetProperty("items").GetArrayLength(), Is.EqualTo(1));
        await CreateAsync(client);
    }

    /// <summary>編集者は削除確認も実行もできず、管理者でも他の参加者の最後の所属を削除できない。</summary>
    [Test]
    public async Task DeletionRequiresManagerAndPreservesEveryMembersLastCollection()
    {
        await using var app = App();
        using var owner = Client(app);
        await SetupAsync(owner);
        var space = await CreateAsync(owner);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<GoodsManager.Web.Auth.AppUser>>();
            var user = new GoodsManager.Web.Auth.AppUser { Id = Guid.NewGuid(), UserName = "editor" };
            Assert.That((await users.CreateAsync(user, Password)).Succeeded, Is.True);
            db.Add(new CollectionMember { CollectionId = space.Id, UserId = user.Id, Role = "editor" });
            await db.SaveChangesAsync();
        }

        var impact = await owner.GetFromJsonAsync<CollectionDeleteImpact>($"/api/v1/collections/{space.Id}/delete-impact") ?? throw new InvalidOperationException();
        Assert.That(impact.CanDelete, Is.False);
        using var refused = await WriteAsync(owner, $"collections/{space.Id}", new
        {
            name = impact.Name,
            confirmation = impact.Confirmation
        }, HttpMethod.Delete, impact.Etag);
        Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));

        using var editor = Client(app);
        using var login = await SendAsync(editor, "auth/password", new
        {
            loginId = "editor",
            password = Password,
            rememberMe = false
        });
        Assert.That(login.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await editor.GetAsync($"/api/v1/collections/{space.Id}/delete-impact")).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        using var forbidden = await WriteAsync(editor, $"collections/{space.Id}", new
        {
            name = impact.Name,
            confirmation = impact.Confirmation
        }, HttpMethod.Delete, impact.Etag);
        Assert.That(forbidden.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    /// <summary>変換処理がDBの外で進んでいても、削除済みコレクションへ画像を再公開しない。</summary>
    [Test]
    public async Task DeletionDuringConversionDoesNotRecreateAssets()
    {
        var converter = new ResumableConverter();
        await using var app = App(services =>
        {
            services.Remove(services.Single(x => x.ServiceType == typeof(IHostedService) && x.ImplementationType == typeof(ImageWorker)));
            services.Replace(ServiceDescriptor.Singleton<IImageConverter>(converter));
        });
        using var client = Client(app);
        await SetupAsync(client);
        var space = await CreateAsync(client);
        client.DefaultRequestHeaders.Remove("X-Collection-Id");
        client.DefaultRequestHeaders.Add("X-Collection-Id", space.Id.ToString("D"));
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
        using var uploadRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/temporary-uploads");
        uploadRequest.Headers.Add("X-CSRF-Token", csrf.GetProperty("token").GetString());
        uploadRequest.Headers.Add("Client-Upload-Key", Guid.NewGuid().ToString("D"));
        var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(ImageConversionTests.Png()), "file", "sample.png");
        uploadRequest.Content = form;
        using var accepted = await client.SendAsync(uploadRequest);
        Assert.That(accepted.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
        var upload = await accepted.Content.ReadFromJsonAsync<JsonElement>();
        var worker = ActivatorUtilities.CreateInstance<ImageWorker>(app.Services);
        var processing = worker.ProcessAsync(upload.GetProperty("id").GetGuid(), CancellationToken.None);
        try
        {
            await converter.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var impact = await client.GetFromJsonAsync<CollectionDeleteImpact>($"/api/v1/collections/{space.Id}/delete-impact") ?? throw new InvalidOperationException();
            using var deleted = await WriteAsync(client, $"collections/{space.Id}", new { name = impact.Name, confirmation = impact.Confirmation }, HttpMethod.Delete, impact.Etag);
            Assert.That(deleted.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }
        finally
        {
            converter.Resume.TrySetResult();
            await processing.WaitAsync(TimeSpan.FromSeconds(5));
        }

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.That(await db.Set<TemporaryUpload>().CountAsync(), Is.Zero);
        Assert.That(await db.Set<ImageAsset>().CountAsync(), Is.Zero);
        Assert.That(await db.Set<ImageFile>().CountAsync(), Is.Zero);
    }

    /// <summary>変換の前後を明示的に同期し、DB書込と競合するタイミングを再現する。</summary>
    private sealed class ResumableConverter : IImageConverter
    {
        /// <summary>入力の受付後、ネイティブ変換へ進む直前の到達通知。</summary>
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <summary>削除確定後に変換結果を返すための再開通知。</summary>
        public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>再開後は実際の変換処理を使い、結果公開の直前まで進める。</summary>
        public async Task<ConvertedImage> ConvertAsync(ImageJob job, CancellationToken token)
        {
            Started.TrySetResult();
            await Resume.Task.WaitAsync(token);
            return ImageConverter.Convert(job);
        }
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
