using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoodsManager.Web.Masters;
using GoodsManager.Web.Products;
using GoodsManager.Web.Search;
using GoodsManager.Web.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static GoodsManager.Tests.AuthenticationTests;

namespace GoodsManager.Tests;

public sealed class SearchProjectionTests
{
    private TestApplication app = null!;
    private HttpClient client = null!;
    private Guid category, work, series;
    [SetUp]
    public async Task Start()
    {
        app = new TestApplication(); client = Client(app); await SetupAsync(client);
        await Db(async db =>
        {
            category = await db.Set<Category>().Select(x => x.Id).FirstAsync();
            work = Guid.NewGuid(); series = Guid.NewGuid();
            db.Add(new Work { Id = work, Name = "作品旧名", NormalizedName = "作品旧名" });
            db.Add(new Series { Id = series, Name = "シリーズ旧名", NormalizedName = "シリーズ旧名" });
            db.Add(new WorkSeries { WorkId = work, SeriesId = series });
            await db.SaveChangesAsync();
        });
    }
    [TearDown]
    public async Task Stop() { client.Dispose(); await app.DisposeAsync(); }
    private async Task Db(Func<AppDbContext, Task> action)
    {
        await using var scope = app.Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }
    private async Task<JsonElement> Create()
    {
        var response = await Write(HttpMethod.Post, "dev/products", new
        {
            product = new { name = "Ｐ０６ 商品", categoryId = category, version = "限定", manufacturerId = (Guid?)null, sellerId = (Guid?)null, memo = "商品メモ", workIds = new[] { work }, characterIds = Array.Empty<Guid>(), tagIds = Array.Empty<Guid>(), identifiers = new[] { new { scheme = "isbn10", value = "0-306-40615-2" } }, links = new[] { new { url = "https://example.com/検索除外" } } },
            initialLot = new { quantity = 3, fields = new { storageLocation = "Ａ棚　上段", purchasePlace = "イベント", memo = "ロットメモ", condition = "unopened" } }
        });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created), await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    private async Task<HttpResponseMessage> Write(HttpMethod method, string path, object body, string? etag = null)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
        using var request = new HttpRequestMessage(method, "/api/v1/" + path);
        request.Headers.Add("X-CSRF-Token", csrf.GetProperty("token").GetString());
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
        if (etag is not null) request.Headers.TryAddWithoutValidation("If-Match", etag);
        request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }
    private async Task<ProductSearchDocument> Document(Guid id)
    {
        ProductSearchDocument value = null!;
        await Db(async db => value = await db.Set<ProductSearchDocument>().AsNoTracking().SingleAsync(x => x.ProductId == id));
        return value;
    }
    private async Task<long> Revision() { long result = 0; await Db(async db => result = (await db.SystemStates.SingleAsync()).DataRevision); return result; }

    [Test]
    public async Task CreationAndInventoryChangesMaintainDerivedTextAndExcludeUrls()
    {
        var p = await Create(); var id = p.GetProperty("id").GetGuid();
        var doc = await Document(id);
        Assert.That(doc.NameNormalized, Is.EqualTo("p06 商品"));
        Assert.That(doc.WorksNormalized, Is.EqualTo("作品旧名"));
        Assert.That(doc.OtherNormalized, Does.Contain("シリーズ旧名").And.Contain("商品メモ").And.Contain("ロットメモ").And.Contain("09780306406157").And.Contain("a棚 上段"));
        Assert.That(doc.OtherNormalized, Does.Not.Contain("検索除外"));
        var lotId = p.GetProperty("inventory").GetProperty("lots")[0].GetProperty("id").GetGuid();
        var response = await Write(HttpMethod.Post, $"products/{id}/inventory-commands", new
        {
            commands = new object[] {
            new { type = "extractItems", lotRef = lotId.ToString(), itemRefs = new[] { "one" } },
            new { type = "updateItem", itemRef = "one", fields = new { memo = "固有メモ", storageLocation = "Ｂ棚" } },
            new { type = "updateLot", lotRef = lotId.ToString(), fields = new { memo = (string?)null, purchasePlace = (string?)null } }
        }
        }, p.GetProperty("etag").GetString());
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        doc = await Document(id);
        Assert.That(doc.OtherNormalized, Does.Contain("固有メモ").And.Contain("b棚").And.Not.Contain("ロットメモ"));
        await Db(async db =>
        {
            var item = await db.Set<InventoryItem>().SingleAsync();
            Assert.That(item.StorageLocationNormalized, Is.EqualTo("b棚"));
            Assert.That(item.PurchasePlaceNormalized, Is.EqualTo("イベント"));
            Assert.That((await db.Set<InventoryLot>().SingleAsync()).PurchasePlaceNormalized, Is.Null);
        });
    }

    [Test]
    public async Task MasterRenameAndRelationshipRemovalRefreshProductsWithoutTouchingProductRevision()
    {
        var p = await Create(); var id = p.GetProperty("id").GetGuid();
        var master = await client.GetFromJsonAsync<JsonElement>($"/api/v1/masters/works/{work}");
        Assert.That((await Write(HttpMethod.Put, $"masters/works/{work}", new { name = "作品新名" }, master.GetProperty("etag").GetString())).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await Document(id)).WorksNormalized, Is.EqualTo("作品新名"));
        var s = await client.GetFromJsonAsync<JsonElement>($"/api/v1/masters/series/{series}");
        Assert.That((await Write(HttpMethod.Post, $"masters/series/{series}/relations", new { addIds = Array.Empty<Guid>(), removeIds = new[] { work } }, s.GetProperty("etag").GetString())).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await Document(id)).OtherNormalized, Does.Not.Contain("シリーズ旧名"));
        var after = await client.GetFromJsonAsync<JsonElement>($"/api/v1/products/{id}");
        Assert.That(after.GetProperty("etag").GetString(), Is.EqualTo(p.GetProperty("etag").GetString()));
        Assert.That(after.GetProperty("updatedAt").GetString(), Is.EqualTo(p.GetProperty("updatedAt").GetString()));
    }

    [Test]
    public async Task FailedProjectionRollsBackProductMasterAndSuccessRecordTogether()
    {
        var p = await Create(); var id = p.GetProperty("id").GetGuid(); var before = await Revision();
        var records = 0; await Db(async db => records = await db.Set<IdempotencyRecord>().CountAsync());
        await Db(db => db.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_projection BEFORE UPDATE ON ProductSearchDocuments BEGIN SELECT RAISE(ABORT,'test'); END"));
        var master = await client.GetFromJsonAsync<JsonElement>($"/api/v1/masters/works/{work}");
        Assert.That((await Write(HttpMethod.Put, $"masters/works/{work}", new { name = "失敗名" }, master.GetProperty("etag").GetString())).StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
        Assert.That((await Document(id)).WorksNormalized, Is.EqualTo("作品旧名"));
        Assert.That(await Revision(), Is.EqualTo(before));
        var lotId = p.GetProperty("inventory").GetProperty("lots")[0].GetProperty("id").GetGuid();
        Assert.That((await Write(HttpMethod.Post, $"products/{id}/inventory-commands", new { commands = new[] { new { type = "updateLot", lotRef = lotId.ToString(), fields = new { memo = "失敗メモ" } } } }, p.GetProperty("etag").GetString())).StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
        Assert.That(await Revision(), Is.EqualTo(before));
        await Db(async db =>
        {
            Assert.That((await db.Set<Work>().SingleAsync()).Name, Is.EqualTo("作品旧名"));
            Assert.That((await db.Set<InventoryLot>().SingleAsync()).Memo, Is.EqualTo("ロットメモ"));
            Assert.That(await db.Set<IdempotencyRecord>().CountAsync(), Is.EqualTo(records));
        });
    }

    [Test]
    public async Task CategorySubtreeMoveAndAncestorRenameRefreshDescendantProductText()
    {
        var p = await Create(); var id = p.GetProperty("id").GetGuid();
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var child = Guid.NewGuid();
        await Db(async db =>
        {
            db.Add(new Category { Id = a, Name = "旧祖先", NormalizedName = "旧祖先" });
            db.Add(new Category { Id = b, Name = "新祖先", NormalizedName = "新祖先" });
            db.Add(new Category { Id = child, Name = "枝カテゴリ", NormalizedName = "枝カテゴリ", ParentCategoryId = a });
            var product = await db.Set<Product>().SingleAsync(); product.CategoryId = child;
            await db.SaveChangesAsync();
        });
        await using (var scope = app.Services.CreateAsyncScope()) await scope.ServiceProvider.GetRequiredService<SearchProjection>().RebuildAllAsync();
        Assert.That((await Document(id)).OtherNormalized, Does.Contain("旧祖先").And.Not.Contain("新祖先"));
        var master = await client.GetFromJsonAsync<JsonElement>($"/api/v1/masters/categories/{child}");
        Assert.That((await Write(HttpMethod.Put, $"masters/categories/{child}", new { name = "枝カテゴリ", parentId = b, sortOrder = 0 }, master.GetProperty("etag").GetString())).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await Document(id)).OtherNormalized, Does.Contain("新祖先").And.Not.Contain("旧祖先"));
        master = await client.GetFromJsonAsync<JsonElement>($"/api/v1/masters/categories/{b}");
        Assert.That((await Write(HttpMethod.Put, $"masters/categories/{b}", new { name = "祖先改名", parentId = (Guid?)null, sortOrder = 0 }, master.GetProperty("etag").GetString())).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await Document(id)).OtherNormalized, Does.Contain("祖先改名").And.Not.Contain("新祖先"));
    }

    [Test]
    public async Task FullRebuildRepairsMissingDerivedDataIncludingTrashWithoutBusinessChanges()
    {
        var p = await Create(); var id = p.GetProperty("id").GetGuid();
        await Db(async db =>
        {
            await db.Set<ProductSearchDocument>().ExecuteDeleteAsync();
            await db.Set<InventoryLot>().ExecuteUpdateAsync(x => x.SetProperty(y => y.StorageLocationNormalized, (string?)null));
            (await db.Set<Product>().SingleAsync()).DeletedAt = app.Clock.GetUtcNow();
            await db.SaveChangesAsync();
        });
        var before = await Revision();
        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SearchProjection>().RebuildAllAsync();
        Assert.That((await Document(id)).NameNormalized, Is.EqualTo("p06 商品"));
        Assert.That(await Revision(), Is.EqualTo(before));
        await Db(async db =>
        {
            var product = await db.Set<Product>().SingleAsync();
            Assert.That(product.Revision, Is.EqualTo(1));
            Assert.That(product.UpdatedAt.ToString("O"), Is.EqualTo(p.GetProperty("updatedAt").GetDateTimeOffset().ToString("O")));
            Assert.That((await db.Set<InventoryLot>().SingleAsync()).StorageLocationNormalized, Is.EqualTo("a棚 上段"));
        });
    }
}
