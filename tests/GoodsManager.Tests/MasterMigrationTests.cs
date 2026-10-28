using System.Net;
using GoodsManager.Web.Masters;
using GoodsManager.Web.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static GoodsManager.Tests.AuthenticationTests;

namespace GoodsManager.Tests;

public sealed class MasterMigrationTests
{
    [Test]
    public async Task RestartPreservesAccountTicketsKeysAndDefaultCollection()
    {
        string directory;
        string cookie;
        Guid userId;
        await using (var first = new TestApplication { PreserveData = true })
        {
            directory = first.DataDirectory;
            using var owner = Client(first);
            await SetupAsync(owner);
            var login = await SendAsync(owner, "auth/password", new { loginId = "owner", password = Password, rememberMe = true });
            cookie = login.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("__Host-GoodsManager=")).Split(';')[0];
            await using var scope = first.Services.CreateAsyncScope();
            userId = (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.SingleAsync()).Id;
        }
        var keys = Directory.GetFiles(Path.Combine(directory, "keys")).ToDictionary(x => Path.GetFileName(x)!, File.ReadAllText);
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(new DatabaseRuntime(directory).ConnectionString).Options;
        await using (var previous = new AppDbContext(options))
        {
            await previous.Database.ExecuteSqlRawAsync("UPDATE SystemState SET DataRevision=77");
        }
        await using var upgraded = new TestApplication(directory);
        using var restored = Client(upgraded);
        restored.DefaultRequestHeaders.Add("Cookie", cookie);
        Assert.That((await restored.GetAsync("/api/v1/auth/session")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        await using var upgradedScope = upgraded.Services.CreateAsyncScope();
        var db = upgradedScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.That((await db.Users.SingleAsync()).Id, Is.EqualTo(userId));
        Assert.That((await db.SystemStates.SingleAsync()).DataRevision, Is.EqualTo(77));
        Assert.That(await db.Set<Category>().CountAsync(), Is.EqualTo(47));
        foreach (var key in keys) Assert.That(File.ReadAllText(Path.Combine(directory, "keys", key.Key!)), Is.EqualTo(key.Value));
        Assert.That(await db.Set<GoodsManager.Web.Collections.CollectionMember>().AnyAsync(x => x.UserId == userId && x.CollectionId == GoodsManager.Web.Collections.CollectionSchema.DefaultId && x.Role == "manager"), Is.True);
        Assert.That(Directory.Exists(Path.Combine(directory, "pre-migration")), Is.False);

    }

    [Test]
    public async Task RestartDoesNotReseedEditedOrDeletedInitialCategories()
    {
        string directory;
        Guid rootId;
        await using (var first = new TestApplication { PreserveData = true })
        {
            directory = first.DataDirectory;
            using var owner = Client(first);
            await SetupAsync(owner);
            await using var scope = first.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var root = await db.Set<Category>().FirstAsync(x => x.ParentCategoryId == null);
            rootId = root.Id;
            // 再起動で初期カテゴリが復活しないことを直接DBで検証する。
            await db.Set<Category>().Where(x => x.ParentCategoryId != null).ExecuteDeleteAsync();
            root.Name = "利用者の編集";
            root.NormalizedName = "利用者の編集";
            root.Revision++;
            await db.SaveChangesAsync();
        }
        await using var second = new TestApplication(directory);
        using var client = Client(second);
        Assert.That((await client.GetAsync("/health/ready")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        await using var afterScope = second.Services.CreateAsyncScope();
        var after = afterScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.That(await after.Set<Category>().CountAsync(), Is.EqualTo(9));
        Assert.That((await after.Set<Category>().SingleAsync(x => x.Id == rootId)).Name, Is.EqualTo("利用者の編集"));
        Assert.That(await DatabaseMaintenance.HasPendingMigrationsAsync(after), Is.False);
    }
}
