using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoodsManager.Web.Masters;
using GoodsManager.Web.Products;
using GoodsManager.Web.Search;
using GoodsManager.Web.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static GoodsManager.Tests.AuthenticationTests;

namespace GoodsManager.Tests;

public sealed class ProductSearchTests
{
    private TestApplication app = null!; private HttpClient client = null!;
    private Guid parent, child, work, series, character, tag, org, product, zero, large;
    [SetUp]
    public async Task Start()
    {
        app = new TestApplication(); client = Client(app); await SetupAsync(client);
        await using var scope = app.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = StorageValues.FromMilliseconds(app.Clock.GetUtcNow().ToUnixTimeMilliseconds());
        parent = Guid.NewGuid(); child = Guid.NewGuid(); work = Guid.NewGuid(); series = Guid.NewGuid(); character = Guid.NewGuid(); tag = Guid.NewGuid(); org = Guid.NewGuid(); product = Guid.NewGuid(); zero = Guid.NewGuid(); large = Guid.NewGuid();
        var secondWork = Guid.NewGuid();
        db.AddRange(new Category { Id = parent, Name = "親分類", NormalizedName = "親分類" }, new Category { Id = child, Name = "子分類", NormalizedName = "子分類", ParentCategoryId = parent });
        db.AddRange(new Work { Id = work, Name = "青空作品", NormalizedName = "青空作品" }, new Work { Id = secondWork, Name = "第二作品", NormalizedName = "第二作品" }, new Series { Id = series, Name = "連作", NormalizedName = "連作" }, new Character { Id = character, Name = "花子", NormalizedName = "花子" }, new Tag { Id = tag, Name = "限定", NormalizedName = "限定" }, new Organization { Id = org, Name = "製造販売", NormalizedName = "製造販売" });
        db.AddRange(new WorkSeries { WorkId = work, SeriesId = series }, new WorkSeries { WorkId = secondWork, SeriesId = series });
        db.AddRange(new Product { Id = product, Name = "Ａ％_\\ 星", NormalizedName = MasterText.Normalize("Ａ％_\\ 星"), CategoryId = child, ManufacturerId = org, SellerId = org, Memo = "商品専用メモ", CreatedAt = now, UpdatedAt = now }, new Product { Id = zero, Name = "所持ゼロ", NormalizedName = "所持ゼロ", CategoryId = parent, CreatedAt = now.AddMinutes(-1), UpdatedAt = now }, new Product { Id = large, Name = "大数量", NormalizedName = "大数量", CategoryId = parent, CreatedAt = now.AddMinutes(-2), UpdatedAt = now });
        var trashed = Guid.NewGuid(); db.Add(new Product { Id = trashed, Name = "ゴミ箱", NormalizedName = "ゴミ箱", CategoryId = parent, DeletedAt = now, CreatedAt = now, UpdatedAt = now });
        db.AddRange(new ProductWork { ProductId = product, WorkId = work }, new ProductWork { ProductId = product, WorkId = secondWork }, new ProductCharacter { ProductId = product, CharacterId = character }, new ProductTag { ProductId = product, TagId = tag });
        foreach (var (scheme, value) in new[] { ("isbn10", "0-306-40615-2"), ("isbn13", "9780306406157") })
        {
            IdentifierCodes.TryNormalize(scheme, value, out var code);
            db.Add(new ProductIdentifier { Id = Guid.NewGuid(), ProductId = product, Scheme = scheme, RawValue = value, NormalizedValue = code!.NormalizedValue, ComparisonValue = code.ComparisonValue, CreatedAt = now, UpdatedAt = now });
        }
        db.Add(new ProductLink { Id = Guid.NewGuid(), ProductId = product, Url = "https://example.com/参照専用", CreatedAt = now, UpdatedAt = now });
        db.AddRange(new InventoryLot { Id = Guid.NewGuid(), ProductId = product, Quantity = 2, Condition = "opened", StorageLocation = "Ａ棚", PurchasePlace = "通販", Memo = "ロット専用", CreatedAt = now, UpdatedAt = now }, new InventoryLot { Id = Guid.NewGuid(), ProductId = product, Quantity = 3, Condition = "unopened", StorageLocation = "Ｂ棚", PurchasePlace = "会場", CreatedAt = now, UpdatedAt = now }, new InventoryItem { Id = Guid.NewGuid(), ProductId = product, Condition = "opened", StorageLocation = "Ｂ棚", PurchasePlace = "通販", Memo = "個体専用", CreatedAt = now, UpdatedAt = now }, new InventoryLot { Id = Guid.NewGuid(), ProductId = large, Quantity = int.MaxValue, CreatedAt = now, UpdatedAt = now }, new InventoryLot { Id = Guid.NewGuid(), ProductId = large, Quantity = int.MaxValue, CreatedAt = now, UpdatedAt = now }, new InventoryLot { Id = Guid.NewGuid(), ProductId = trashed, Quantity = 7, StorageLocation = "削除場所", CreatedAt = now, UpdatedAt = now });
        await db.SaveChangesAsync(); await scope.ServiceProvider.GetRequiredService<SearchProjection>().RebuildAllAsync();
    }
    [TearDown] public async Task Stop() { client.Dispose(); await app.DisposeAsync(); }
    private async Task<JsonElement> Search(string query = "")
    {
        var response = await client.GetAsync("/api/v1/products?" + query);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    private static Guid[] Ids(JsonElement page) => page.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToArray();
    private async Task AssertOnly(string query, params Guid[] expected) => Assert.That(Ids(await Search(query)), Is.EquivalentTo(expected), query);
    [Test]
    public async Task CountsQuantitiesAndCollectionSummaryDoNotMultiplyByRelationsOrCodes()
    {
        var page = await Search(); Assert.That(page.GetProperty("totalCount").GetInt64(), Is.EqualTo(2));
        var p = page.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == product);
        Assert.That(p.GetProperty("totalQuantity").GetInt64(), Is.EqualTo(6)); Assert.That(p.GetProperty("works").GetArrayLength(), Is.EqualTo(2));
        Assert.That(p.GetProperty("conditions").EnumerateArray().Select(x => x.GetString()), Is.EquivalentTo(new[] { "opened", "unopened" }));
        Assert.That(page.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == large).GetProperty("totalQuantity").GetInt64(), Is.EqualTo(4294967294));
        var summary = await client.GetFromJsonAsync<JsonElement>("/api/v1/collection-summary");
        Assert.That(summary.GetProperty("productCount").GetInt64(), Is.EqualTo(2)); Assert.That(summary.GetProperty("totalQuantity").GetInt64(), Is.EqualTo(4294967300));
        await AssertOnly("inventory=zero", zero); await AssertOnly("inventory=all", product, zero, large);
        await AssertOnly("quantityMin=6&quantityMax=6", product); await AssertOnly("quantityMin=2147483648", large);
        await AssertOnly("conditions=unset", large);
    }
    [Test]
    public async Task AllTextFieldsUseNormalizedAndTermsAndLiteralMetacharactersWithoutUrls()
    {
        foreach (var text in new[] { "a％_\\", "青空作品　商品専用メモ", "限定 花子", "ロット専用 個体専用", "連作 製造販売", "親分類 星" })
            await AssertOnly("q=" + Uri.EscapeDataString(text), product);
        await AssertOnly("q=" + Uri.EscapeDataString("参照専用"));
        await AssertOnly("q=" + Uri.EscapeDataString("星 存在しない"));
        await AssertOnly("q=" + Uri.EscapeDataString("' OR 1=1 --"));
        await AssertOnly("q=%25", product); await AssertOnly("q=_", product); await AssertOnly("q=%5C", product);
    }
    [Test]
    public async Task BarcodeAndFreeTextFindEquivalentCodesOnceAndChecksumIsEnforced()
    {
        foreach (var code in new[] { "0306406152", "０-３０６-４０６１５-２", "9780306406157", "0 306 40615 2" })
        {
            await AssertOnly("barcode=" + Uri.EscapeDataString(code), product);
            await AssertOnly("q=" + Uri.EscapeDataString(code), product);
        }
        await AssertOnly("barcode=4900000000009");
        Assert.That((await client.GetAsync("/api/v1/products?barcode=9780306406158")).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That((await Search("q=9780306406158")).GetProperty("totalCount").GetInt64(), Is.Zero);
    }
    [Test]
    public async Task SameFieldIsOrAcrossFieldsAndAndCategoryIncludesItselfAndDescendants()
    {
        await AssertOnly($"categoryIds={parent}&inventory=all", product, zero, large);
        await AssertOnly($"categoryIds={child}&inventory=all", product);
        await AssertOnly($"workIds={work}&workIds={Guid.NewGuid()}&seriesIds={series}&characterIds={character}&tagIds={tag}&manufacturerIds={org}&sellerIds={org}", product);
        await AssertOnly($"workIds={Guid.NewGuid()}&tagIds={tag}");
        await AssertOnly($"seriesIds={series}", product);
    }
    [Test]
    public async Task AllOwnershipFiltersMustMatchOneLotOrOneItem()
    {
        await AssertOnly("conditions=opened&storageLocation=" + Uri.EscapeDataString("Ｂ棚") + "&purchasePlace=" + Uri.EscapeDataString("会場"));
        await AssertOnly("conditions=opened&storageLocation=" + Uri.EscapeDataString("b棚") + "&purchasePlace=" + Uri.EscapeDataString("通販"), product);
        await AssertOnly("conditions=unopened&conditions=opened&storageLocation=" + Uri.EscapeDataString("Ｂ棚") + "&purchasePlace=" + Uri.EscapeDataString("会場"), product);
        await AssertOnly("hasImages=true"); await AssertOnly("hasImages=false", product, large);
    }
    [TestCase("page=0")]
    [TestCase("pageSize=26")]
    [TestCase("sort=unknown")]
    [TestCase("inventory=trash")]
    [TestCase("hasImages=1")]
    [TestCase("quantityMin=-1")]
    [TestCase("quantityMax=9007199254740992")]
    [TestCase("quantityMin=3&quantityMax=2")]
    [TestCase("conditions=invalid")]
    [TestCase("workIds=bad")]
    [TestCase("q=a&q=b")]
    [TestCase("q=%00")]
    public async Task InvalidQueriesAre400(string query) => Assert.That((await client.GetAsync("/api/v1/products?" + query)).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    [Test]
    public async Task CodePointAndIdLimitsRejectRatherThanTruncate()
    {
        Assert.That((await client.GetAsync("/api/v1/products?q=" + Uri.EscapeDataString(string.Concat(Enumerable.Repeat("😀", 200))))).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await client.GetAsync("/api/v1/products?q=" + Uri.EscapeDataString(string.Concat(Enumerable.Repeat("😀", 201))))).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That((await client.GetAsync("/api/v1/products?" + string.Join('&', Enumerable.Repeat("workIds=" + work, 101)))).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        using var anonymous = Client(app); Assert.That((await anonymous.GetAsync("/api/v1/products")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That((await anonymous.GetAsync("/api/v1/collection-summary")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That((await anonymous.GetAsync("/api/v1/suggestions/ownership?field=storageLocation")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }
    [Test]
    public async Task SuggestionsUseOnlyCurrentValuesAndNormalizedFiltering()
    {
        var suggestions = await client.GetFromJsonAsync<JsonElement>("/api/v1/suggestions/ownership?field=storageLocation&q=" + Uri.EscapeDataString("a"));
        Assert.That(suggestions.GetProperty("values").EnumerateArray().Select(x => x.GetString()), Is.EqualTo(new[] { "Ａ棚" }));
        await using var scope = app.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Set<InventoryLot>().Where(x => x.StorageLocation == "Ａ棚").ExecuteDeleteAsync();
        suggestions = await client.GetFromJsonAsync<JsonElement>("/api/v1/suggestions/ownership?field=storageLocation");
        Assert.That(suggestions.GetProperty("values").EnumerateArray().Select(x => x.GetString()), Is.EqualTo(new[] { "Ｂ棚" }));
        Assert.That((await client.GetAsync("/api/v1/suggestions/ownership?field=memo")).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That((await client.GetAsync("/api/v1/suggestions/ownership?field=storageLocation&limit=21")).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }
    [Test]
    public async Task StablePagingClampsEmptyAndOutOfRangeAndRanksExactCodeBeforeName()
    {
        await using var scope = app.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = StorageValues.FromMilliseconds(app.Clock.GetUtcNow().ToUnixTimeMilliseconds());
        var expected = new List<Guid>();
        for (var n = 0; n < 53; n++)
        {
            var id = Guid.NewGuid(); expected.Add(id);
            db.Add(new Product { Id = id, Name = "同名", NormalizedName = "同名", CategoryId = parent, CreatedAt = now, UpdatedAt = now });
            db.Add(new InventoryLot { Id = Guid.NewGuid(), ProductId = id, Quantity = 1, CreatedAt = now, UpdatedAt = now });
        }
        var codeName = Guid.NewGuid(); db.Add(new Product { Id = codeName, Name = "9780306406157", NormalizedName = "9780306406157", CategoryId = parent, CreatedAt = now.AddMinutes(2), UpdatedAt = now });
        db.Add(new InventoryLot { Id = Guid.NewGuid(), ProductId = codeName, Quantity = 1, CreatedAt = now, UpdatedAt = now });
        await db.SaveChangesAsync(); await scope.ServiceProvider.GetRequiredService<SearchProjection>().RebuildAllAsync();
        var all = new List<Guid>();
        for (var page = 1; page <= 3; page++) all.AddRange(Ids(await Search("q=" + Uri.EscapeDataString("同名") + "&pageSize=25&page=" + page)));
        Assert.That(all, Is.EqualTo(expected.OrderBy(x => x.ToString("D"), StringComparer.Ordinal)));
        Assert.That((await Search("q=" + Uri.EscapeDataString("同名") + "&pageSize=25&page=2147483647")).GetProperty("page").GetInt32(), Is.EqualTo(3));
        var empty = await Search("q=no-such-product&page=99"); Assert.That(empty.GetProperty("page").GetInt32(), Is.EqualTo(1)); Assert.That(empty.GetProperty("totalPages").GetInt64(), Is.Zero);
        Assert.That(Ids(await Search("q=9780306406157")), Is.EqualTo(new[] { product, codeName }));
        var summary = await client.GetFromJsonAsync<JsonElement>("/api/v1/collection-summary"); Assert.That(summary.GetProperty("recentProducts").GetArrayLength(), Is.EqualTo(10));
    }
    [Test]
    public async Task ExistingReadSnapshotKeepsCountPageAndDataRevisionConsistentAcrossWriterCommit()
    {
        await using var scope = app.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.OpenConnectionAsync();
        await using var native = ((SqliteConnection)db.Database.GetDbConnection()).BeginTransaction(deferred: true);
        await using var transaction = await db.Database.UseTransactionAsync(native);
        var revision = (await db.SystemStates.AsNoTracking().SingleAsync()).DataRevision;
        await using (var writeScope = app.Services.CreateAsyncScope())
        {
            var writer = writeScope.ServiceProvider.GetRequiredService<AppDbContext>();
            await using var write = await writer.Database.BeginTransactionAsync();
            await writer.Set<Product>().Where(x => x.Id == product).ExecuteUpdateAsync(x => x.SetProperty(y => y.DeletedAt, app.Clock.GetUtcNow()));
            await writer.SystemStates.ExecuteUpdateAsync(x => x.SetProperty(y => y.DataRevision, y => y.DataRevision + 1));
            await write.CommitAsync();
        }
        var before = await scope.ServiceProvider.GetRequiredService<ProductSearch>().PageAsync(new SearchQuery(), default);
        Assert.That(before.TotalCount, Is.EqualTo(2)); Assert.That(before.Items.Select(x => x.Id), Does.Contain(product));
        Assert.That(before.DataRevision, Is.EqualTo(revision.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        await native.CommitAsync();
        var after = await Search(); Assert.That(after.GetProperty("totalCount").GetInt64(), Is.EqualTo(1));
        Assert.That(after.GetProperty("dataRevision").GetString(), Is.EqualTo((revision + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Test]
    public async Task RelevanceAndBinaryNameAndQuantityOrdersAreStable()
    {
        await using var scope = app.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = StorageValues.FromMilliseconds(app.Clock.GetUtcNow().ToUnixTimeMilliseconds());
        var w = Guid.NewGuid(); var c = Guid.NewGuid();
        db.Add(new Work { Id = w, Name = "needle", NormalizedName = "needle" });
        db.Add(new Character { Id = c, Name = "needle", NormalizedName = "needle" });
        var ids = new List<Guid>();
        for (var n = 0; n < 5; n++)
        {
            var id = Guid.NewGuid(); ids.Add(id); var name = n switch { 0 => "ＮＥＥＤＬＥ", 1 => "a needle b", _ => "other" + n };
            db.Add(new Product { Id = id, Name = name, NormalizedName = MasterText.Normalize(name), CategoryId = parent, Memo = n == 4 ? "needle" : null, CreatedAt = now.AddMinutes(n), UpdatedAt = now.AddMinutes(-n) });
            db.Add(new InventoryLot { Id = Guid.NewGuid(), ProductId = id, Quantity = n + 1, CreatedAt = now, UpdatedAt = now });
            if (n == 2) db.Add(new ProductWork { ProductId = id, WorkId = w });
            if (n == 3) db.Add(new ProductCharacter { ProductId = id, CharacterId = c });
        }
        await db.SaveChangesAsync(); await scope.ServiceProvider.GetRequiredService<SearchProjection>().RebuildAllAsync();
        Assert.That(Ids(await Search("q=needle")), Is.EqualTo(ids));
        Assert.That(Ids(await Search("q=needle&sort=quantityAsc")), Is.EqualTo(ids));
        Assert.That(Ids(await Search("q=needle&sort=quantityDesc")), Is.EqualTo(ids.AsEnumerable().Reverse()));
        Assert.That(Ids(await Search("q=needle&sort=nameAsc")), Is.EqualTo(new[] { ids[1], ids[0], ids[2], ids[3], ids[4] }));
        Assert.That(Ids(await Search("q=needle&sort=nameDesc")), Is.EqualTo(new[] { ids[4], ids[3], ids[2], ids[0], ids[1] }));
        Assert.That(Ids(await Search("q=needle&sort=createdAsc")), Is.EqualTo(ids));
        Assert.That(Ids(await Search("q=needle&sort=updatedDesc")), Is.EqualTo(ids));
    }

    [Test]
    public async Task RepresentativeFiveHundredProductsUseIndexedOwnershipAndBoundedPageWithoutNPlusOne()
    {
        await using var scope = app.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = StorageValues.FromMilliseconds(app.Clock.GetUtcNow().ToUnixTimeMilliseconds());
        for (var n = 0; n < 500; n++)
        {
            var id = Guid.NewGuid(); db.Add(new Product { Id = id, Name = $"測定{n:D4}", NormalizedName = $"測定{n:D4}", CategoryId = child, CreatedAt = now.AddSeconds(n), UpdatedAt = now });
            db.Add(new InventoryLot { Id = Guid.NewGuid(), ProductId = id, Quantity = n + 1, CreatedAt = now, UpdatedAt = now });
        }
        await db.SaveChangesAsync(); await scope.ServiceProvider.GetRequiredService<SearchProjection>().RebuildAllAsync();
        var watch = Stopwatch.StartNew(); var page = await Search("q=" + Uri.EscapeDataString("測定") + "&sort=quantityDesc&pageSize=25&page=2"); watch.Stop();
        Assert.That(page.GetProperty("totalCount").GetInt64(), Is.EqualTo(500)); Assert.That(page.GetProperty("items")[0].GetProperty("totalQuantity").GetInt64(), Is.EqualTo(475));
        TestContext.Out.WriteLine($"P06 representative 503 active products: {watch.ElapsedMilliseconds} ms");
        Assert.That(watch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(5)));
        await db.Database.ExecuteSqlRawAsync("ANALYZE");
        await db.Database.OpenConnectionAsync(); await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "EXPLAIN QUERY PLAN SELECT p.ProductId FROM Products p WHERE p.DeletedAt IS NULL ORDER BY p.CreatedAt DESC,p.ProductId ASC LIMIT 50";
        var steps = new List<string>(); await using (var reader = await command.ExecuteReaderAsync()) while (await reader.ReadAsync()) steps.Add(reader.GetString(3));
        TestContext.Out.WriteLine(string.Join("; ", steps)); Assert.That(steps.Any(x => x.Contains("IX_Products_CreatedAt_ProductId", StringComparison.Ordinal)), Is.True);
        Assert.That(steps.Any(x => x.Contains("TEMP B-TREE", StringComparison.Ordinal)), Is.False);
    }
}
