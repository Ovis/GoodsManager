using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoodsManager.Web.Masters;
using GoodsManager.Web.Products;
using GoodsManager.Web.Search;
using GoodsManager.Web.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using static GoodsManager.Tests.AuthenticationTests;

namespace GoodsManager.Tests;

public sealed class SearchMigrationTests
{
    [Test]
    public async Task P05UpgradePreservesTicketLotsItemsAndBusinessNumbersAndRebuildsSearch()
    {
        string directory, cookie; Guid productId, lotId, itemId; long revision;
        const string previousMigration = "20261018065826_ProductFoundation";
        await using (var first = new TestApplication { PreserveData = true })
        {
            directory = first.DataDirectory;
            using var owner = Client(first); await SetupAsync(owner);
            var login = await SendAsync(owner, "auth/password", new { loginId = "owner", password = Password, rememberMe = true });
            cookie = login.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("__Host-GoodsManager=")).Split(';')[0];
            await using var scope = first.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var category = await db.Set<Category>().Select(x => x.Id).FirstAsync();
            productId = Guid.NewGuid(); lotId = Guid.NewGuid(); itemId = Guid.NewGuid();
            var now = StorageValues.FromMilliseconds(first.Clock.GetUtcNow().ToUnixTimeMilliseconds());
            db.Add(new Product { Id = productId, Name = "移行商品", NormalizedName = "移行商品", CategoryId = category, Memo = "保存メモ", Revision = 7, CreatedAt = now, UpdatedAt = now });
            db.Add(new InventoryLot { Id = lotId, ProductId = productId, Quantity = 2, StorageLocation = "Ａ棚", PurchasePlace = "会場", Memo = "ロット", CreatedAt = now, UpdatedAt = now });
            db.Add(new InventoryItem { Id = itemId, ProductId = productId, StorageLocation = "Ｂ棚", PurchasePlace = "通販", Condition = "opened", Memo = "個体", CreatedAt = now, UpdatedAt = now });
            await db.SaveChangesAsync();
            revision = (await db.SystemStates.SingleAsync()).DataRevision;
        }
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(new DatabaseRuntime(directory).ConnectionString).Options;
        await using (var previous = new AppDbContext(options)) await previous.GetService<IMigrator>().MigrateAsync(previousMigration);
        var keys = Directory.GetFiles(Path.Combine(directory, "keys")).ToDictionary(x => Path.GetFileName(x)!, File.ReadAllText);
        await using var upgraded = new TestApplication(directory);
        using var restored = Client(upgraded); restored.DefaultRequestHeaders.Add("Cookie", cookie);
        Assert.That((await restored.GetAsync("/api/v1/auth/session")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var detail = await restored.GetFromJsonAsync<JsonElement>($"/api/v1/products/{productId}");
        Assert.That(detail.GetProperty("revision").GetString(), Is.EqualTo("7"));
        Assert.That(detail.GetProperty("totalQuantity").GetInt64(), Is.EqualTo(3));
        await using var afterScope = upgraded.Services.CreateAsyncScope();
        var after = afterScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.That((await after.SystemStates.SingleAsync()).DataRevision, Is.EqualTo(revision));
        var lot = await after.Set<InventoryLot>().SingleAsync(); var item = await after.Set<InventoryItem>().SingleAsync();
        Assert.That(lot.Id, Is.EqualTo(lotId)); Assert.That(item.Id, Is.EqualTo(itemId));
        Assert.That(lot.Quantity, Is.EqualTo(2)); Assert.That(item.Memo, Is.EqualTo("個体"));
        Assert.That(lot.StorageLocationNormalized, Is.EqualTo("a棚")); Assert.That(item.StorageLocationNormalized, Is.EqualTo("b棚"));
        var projection = await after.Set<ProductSearchDocument>().SingleAsync();
        Assert.That(projection.OtherNormalized, Does.Contain("保存メモ").And.Contain("ロット").And.Contain("個体"));
        Assert.That(after.Database.HasPendingModelChanges(), Is.False);
        foreach (var key in keys) Assert.That(File.ReadAllText(Path.Combine(directory, "keys", key.Key!)), Is.EqualTo(key.Value));
        using var zip = ZipFile.OpenRead(Directory.GetFiles(Path.Combine(directory, "pre-migration"), "*.zip").Single());
        var file = Path.Combine(directory, "before.db"); zip.GetEntry("goods-manager.db")!.ExtractToFile(file);
        await using var snapshot = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={file};Pooling=False").Options);
        Assert.That((await snapshot.Database.GetAppliedMigrationsAsync()).Last(), Is.EqualTo(previousMigration));
        // 旧スキーマに新しい正規化列はないため、正本の旧列をSQLで検証する。
        Assert.That(await snapshot.Database.SqlQueryRaw<long>("SELECT SUM(Quantity) AS Value FROM InventoryLots").SingleAsync(), Is.EqualTo(2));
        Assert.That(await snapshot.Database.SqlQueryRaw<long>("SELECT COUNT(*) AS Value FROM InventoryItems").SingleAsync(), Is.EqualTo(1));
        Assert.That((await snapshot.SystemStates.SingleAsync()).DataRevision, Is.EqualTo(revision));
    }
}
