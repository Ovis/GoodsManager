using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoodsManager.Web.Products;
using GoodsManager.Web.Masters;
using GoodsManager.Web.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using static GoodsManager.Tests.AuthenticationTests;
namespace GoodsManager.Tests;

public sealed class MaintenanceMigrationTests
{
    /// <summary>P09の成功応答・商品・鍵・クッキーを、更新前バックアップとともに保持</summary>
    [Test]
    public async Task P09DataAndSuccessReplaySurviveTheDeletionResponseUpgrade()
    {
        string directory, cookie, etag; Guid productId, key = Guid.NewGuid();
        await using (var first = new TestApplication { PreserveData = true })
        {
            directory = first.DataDirectory; using var owner = Client(first); await SetupAsync(owner);
            var login = await SendAsync(owner, "auth/password", new { loginId = "owner", password = Password, rememberMe = true }); cookie = login.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("__Host-GoodsManager=")).Split(';')[0];
            var category = (await owner.GetFromJsonAsync<JsonElement>("/api/v1/masters/categories")).GetProperty("items")[0].GetProperty("id").GetGuid();
            var csrf = await owner.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf"); using var create = new HttpRequestMessage(HttpMethod.Post, "/api/v1/dev/products") { Content = JsonContent.Create(new { product = new { name = "移行確認商品", categoryId = category, workIds = Array.Empty<Guid>(), characterIds = Array.Empty<Guid>(), tagIds = Array.Empty<Guid>(), identifiers = Array.Empty<object>(), links = Array.Empty<object>() }, initialLot = new { quantity = 3, fields = new { condition = (string?)null, storageLocation = (string?)null, purchasePlace = (string?)null, memo = (string?)null } } }) }; create.Headers.Add("X-CSRF-Token", csrf.GetProperty("token").GetString()); create.Headers.Add("Idempotency-Key", key.ToString()); var result = await owner.SendAsync(create); Assert.That(result.StatusCode, Is.EqualTo(HttpStatusCode.Created), await result.Content.ReadAsStringAsync()); var product = await result.Content.ReadFromJsonAsync<JsonElement>(); productId = product.GetProperty("id").GetGuid(); etag = product.GetProperty("etag").GetString()!;
        }
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(new DatabaseRuntime(directory).ConnectionString).Options;
        await using (var previous = new AppDbContext(options)) { await previous.GetService<IMigrator>().MigrateAsync("20261018100058_ImageFoundation"); await previous.Database.ExecuteSqlRawAsync("UPDATE SystemState SET DataRevision=77"); }
        var keys = Directory.GetFiles(Path.Combine(directory, "keys")).ToDictionary(x => Path.GetFileName(x)!, File.ReadAllText);
        await using var upgraded = new TestApplication(directory); using var client = Client(upgraded); client.DefaultRequestHeaders.Add("Cookie", cookie); Assert.That((await client.GetAsync("/api/v1/auth/session")).StatusCode, Is.EqualTo(HttpStatusCode.OK)); var after = await client.GetFromJsonAsync<JsonElement>($"/api/v1/products/{productId}"); Assert.That(after.GetProperty("etag").GetString(), Is.EqualTo(etag)); Assert.That(after.GetProperty("totalQuantity").GetInt64(), Is.EqualTo(3));
        await using var scope = upgraded.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); Assert.That((await db.SystemStates.SingleAsync()).DataRevision, Is.EqualTo(77)); Assert.That(await db.Set<IdempotencyRecord>().AnyAsync(x => x.Key == key && x.ResponseStatus == 201), Is.True); Assert.That(db.Database.HasPendingModelChanges(), Is.False); foreach (var pair in keys) Assert.That(File.ReadAllText(Path.Combine(directory, "keys", pair.Key!)), Is.EqualTo(pair.Value));
        using var zip = ZipFile.OpenRead(Directory.GetFiles(Path.Combine(directory, "pre-migration"), "*.zip").Single()); var beforePath = Path.Combine(directory, "before.db"); zip.GetEntry("goods-manager.db")!.ExtractToFile(beforePath); await using var snapshot = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={beforePath};Pooling=False").Options); Assert.That((await snapshot.Database.GetAppliedMigrationsAsync()).Last(), Is.EqualTo("20261018100058_ImageFoundation")); Assert.That(await snapshot.Set<Product>().AnyAsync(x => x.Id == productId), Is.True);
    }
}
