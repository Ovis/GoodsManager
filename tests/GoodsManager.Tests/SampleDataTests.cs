using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoodsManager.SampleData;
using GoodsManager.Web.Images;
using GoodsManager.Web.Products;
using GoodsManager.Web.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using static GoodsManager.Tests.AuthenticationTests;

namespace GoodsManager.Tests;

/// <summary>サンプルの実API投入、繰り返し、通信断後の再開、接続先の分離を検証する。</summary>
public sealed class SampleDataTests
{
    private static TestApplication App() => new()
    {
        ConfigureTestServices = services =>
        {
            services.RemoveAll<IImageConverter>();
            services.AddSingleton<IImageConverter, TestConverter>();
        }
    };
    private sealed class TestConverter : IImageConverter
    {
        public Task<ConvertedImage> ConvertAsync(ImageJob job, CancellationToken token)
        {
            Directory.CreateDirectory(job.OutputDirectory);
            return Task.FromResult(ImageConverter.Convert(job));
        }
    }
    private static SampleCatalog Catalog(params int[] ids)
    {
        var catalog = SampleCatalog.Load();
        return catalog with { Products = catalog.Products.Where(product => ids.Contains(product.Id)).ToArray() };
    }
    private static Task<ImportResult> Import(HttpClient client, SampleCatalog catalog, string state) => new SampleDataImporter(client, catalog, state, TextWriter.Null).RunAsync();
    private static async Task<long> Revision(TestApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<AppDbContext>().SystemStates.AsNoTracking().SingleAsync()).DataRevision;
    }

    [TestCase("https://localhost:7449", true)]
    [TestCase("https://127.0.0.1:7449", true)]
    [TestCase("https://[::1]:7449", true)]
    [TestCase("http://localhost:7449", false)]
    [TestCase("https://example.com", true)]
    [TestCase("https://localhost.example.com", false)]
    [TestCase("https://example.com:8443/", true)]
    [TestCase("https://192.168.1.10:8443", true)]
    [TestCase("https://[2001:db8::1]:8443", true)]
    [TestCase("https://localhost.example.com", true)]
    [TestCase("https://127.0.0.1.example.com", true)]
    [TestCase("https://user:secret@localhost", false)]
    [TestCase("https://localhost/account", false)]
    [TestCase("https://localhost?target=x", false)]
    [TestCase("http://example.com", false)]
    [TestCase("https://user:secret@example.com", false)]
    [TestCase("https://example.com/api", false)]
    [TestCase("https://example.com?target=x", false)]
    [TestCase("https://example.com#fragment", false)]
    [TestCase("", false)]
    public void TargetIsLimitedToHttpsOrigins(string target, bool allowed)
    {
        if (allowed) Assert.That(LocalTarget.Parse(target).Scheme, Is.EqualTo("https"));
        else Assert.Throws<ArgumentException>(() => LocalTarget.Parse(target));
    }

    [Test]
    public async Task FullCatalogImportsAndAnotherJournalDoesNotDuplicateOrOverwriteEdits()
    {
        await using var app = App(); using var client = Client(app); await SetupAsync(client);
        var state = Path.Combine(app.DataDirectory, "sample-journal");
        Assert.That((await Import(client, SampleCatalog.Load(), state)).Added, Is.EqualTo(64));
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.That(await db.Set<Product>().CountAsync(x => x.DeletedAt == null), Is.EqualTo(62));
            Assert.That(await db.Set<Product>().CountAsync(x => x.DeletedAt != null), Is.EqualTo(2));
            Assert.That(await db.Set<InventoryItem>().CountAsync(), Is.EqualTo(10));
            Assert.That(await db.Set<ProductImage>().CountAsync(), Is.EqualTo(67));
            var first = await db.Set<Product>().FirstAsync();
            first.Name = "利用者が編集した名前"; await db.SaveChangesAsync();
        }
        var before = await Revision(app);
        Assert.That(await Import(client, SampleCatalog.Load(), state), Is.EqualTo(new ImportResult(0, 64)));
        // 実行記録がなくても、通常商品とゴミ箱のサンプル番号で既存データを認識する。
        Assert.That(await Import(client, SampleCatalog.Load(), Path.Combine(state, "another")), Is.EqualTo(new ImportResult(0, 64)));
        Assert.That(await Revision(app), Is.EqualTo(before));
        var barcode = await client.GetFromJsonAsync<JsonElement>("/api/v1/products?barcode=" + SampleCatalog.Barcode(1));
        Assert.That(barcode.GetProperty("totalCount").GetInt64(), Is.EqualTo(1));
        var firstResult = barcode.GetProperty("items")[0];
        using var image = await client.GetAsync(firstResult.GetProperty("primaryImage").GetProperty("thumbnailUrl").GetString());
        Assert.That(image.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var files = Directory.EnumerateFiles(state, "*.json", SearchOption.AllDirectories).Select(File.ReadAllText);
        Assert.That(string.Join('\n', files), Does.Not.Contain(Password).And.Not.Contain("__Host-GoodsManager"));
    }

    [Test]
    public async Task LostCreationResponseResumesWithTheSameRequestAndDoesNotAddAnotherProduct()
    {
        await using var app = App(); using var owner = Client(app); await SetupAsync(owner);
        using var handler = new LostResponseHandler(app.Server.CreateHandler());
        using var client = new HttpClient(handler) { BaseAddress = owner.BaseAddress };
        client.DefaultRequestHeaders.Add("X-Collection-Id", GoodsManager.Web.Collections.CollectionSchema.DefaultId.ToString("D"));
        await new SampleApi(client).SendAsync("auth/password", new { loginId = "owner", password = Password, rememberMe = false });
        var state = Path.Combine(app.DataDirectory, "resume-journal");
        Assert.ThrowsAsync<HttpRequestException>(async () => await Import(client, Catalog(1), state));
        Assert.That(handler.Lost, Is.True);
        var result = await Import(owner, Catalog(1), state);
        Assert.That(result.Added, Is.EqualTo(1));
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.That(await db.Set<Product>().CountAsync(), Is.EqualTo(1));
        Assert.That(await db.Set<InventoryItem>().CountAsync(), Is.EqualTo(1));
    }

    private sealed class LostResponseHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        private readonly CookieContainer cookies = new();
        public bool Lost { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var uri = request.RequestUri!;
            var cookie = cookies.GetCookieHeader(uri);
            if (cookie.Length > 0) request.Headers.Add("Cookie", cookie);
            var response = await base.SendAsync(request, token);
            if (response.Headers.TryGetValues("Set-Cookie", out var values)) foreach (var value in values) cookies.SetCookies(uri, value);
            if (!Lost && request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/api/v1/products" && response.IsSuccessStatusCode)
            {
                Lost = true; response.Dispose(); throw new HttpRequestException("試験用の応答切断");
            }
            return response;
        }
    }

    [Test]
    public async Task RecreatedAccountUsesSeparateJournalOnTheSameOrigin()
    {
        await using var first = App(); using var firstClient = Client(first); await SetupAsync(firstClient);
        var state = Path.Combine(first.DataDirectory, "shared-journal");
        Assert.That((await Import(firstClient, Catalog(2), state)).Added, Is.EqualTo(1));
        await using var second = App(); using var secondClient = Client(second); await SetupAsync(secondClient);
        Assert.That((await Import(secondClient, Catalog(2), state)).Added, Is.EqualTo(1));
        Assert.That(Directory.GetDirectories(state).Length, Is.EqualTo(2));
    }
    [Test]
    public async Task SameAccountUsesSeparateJournalsAndImportsForEachCollection()
    {
        await using var app = App(); using var client = Client(app); await SetupAsync(client);
        var state = Path.Combine(app.DataDirectory, "collection-journals"); var catalog = Catalog(2);
        Assert.That((await Import(client, catalog, state)).Added, Is.EqualTo(1));
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/collections") { Content = JsonContent.Create(new { name = "サンプル用" }) }; request.Headers.Add("X-CSRF-Token", csrf.GetProperty("token").GetString()); request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var response = await client.SendAsync(request); Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString(); client.DefaultRequestHeaders.Remove("X-Collection-Id"); client.DefaultRequestHeaders.Add("X-Collection-Id", id);
        Assert.That((await Import(client, catalog, state)).Added, Is.EqualTo(1));
        Assert.That(await Import(client, catalog, state), Is.EqualTo(new ImportResult(0, 1))); Assert.That(Directory.GetDirectories(state).Length, Is.EqualTo(2));
        await using var scope = app.Services.CreateAsyncScope(); Assert.That(await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<Product>().CountAsync(), Is.EqualTo(2));
    }

}
