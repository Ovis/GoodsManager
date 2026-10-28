using System.Net;
using GoodsManager.Web.Masters;
using GoodsManager.Web.Products;
using GoodsManager.Web.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static GoodsManager.Tests.AuthenticationTests;

namespace GoodsManager.Tests;

public sealed class ProductMigrationTests
{
    [Test]
    public async Task RestartPreservesMastersBusinessRevisionAndAuthentication()
    {
        string directory, cookie;
        var workId = Guid.NewGuid();
        await using (var first = new TestApplication { PreserveData = true })
        {
            directory = first.DataDirectory;
            using var owner = Client(first);
            await SetupAsync(owner);
            var login = await SendAsync(owner, "auth/password", new { loginId = "owner", password = Password, rememberMe = true });
            cookie = login.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("__Host-GoodsManager=")).Split(';')[0];
            await using var scope = first.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Add(new Work { Id = workId, Name = "移行前作品", NormalizedName = "移行前作品" });
            await db.SaveChangesAsync();
        }
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(new DatabaseRuntime(directory).ConnectionString).Options;
        await using (var previous = new AppDbContext(options))
        {
            await previous.Database.ExecuteSqlRawAsync("UPDATE SystemState SET DataRevision=77");
        }
        var keys = Directory.GetFiles(Path.Combine(directory, "keys")).ToDictionary(x => Path.GetFileName(x)!, File.ReadAllText);
        await using var upgraded = new TestApplication(directory);
        using var restored = Client(upgraded);
        restored.DefaultRequestHeaders.Add("Cookie", cookie);
        Assert.That((await restored.GetAsync("/api/v1/auth/session")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        await using var afterScope = upgraded.Services.CreateAsyncScope();
        var after = afterScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.That((await after.Set<Work>().SingleAsync(x => x.Id == workId)).Name, Is.EqualTo("移行前作品"));
        Assert.That((await after.SystemStates.SingleAsync()).DataRevision, Is.EqualTo(77));
        Assert.That(await after.Set<Category>().CountAsync(), Is.EqualTo(47));
        Assert.That(await after.Set<Product>().CountAsync(), Is.Zero);
        Assert.That(after.Database.HasPendingModelChanges(), Is.False);
        foreach (var key in keys) Assert.That(File.ReadAllText(Path.Combine(directory, "keys", key.Key!)), Is.EqualTo(key.Value));
        Assert.That(Directory.Exists(Path.Combine(directory, "pre-migration")), Is.False);
    }
}
