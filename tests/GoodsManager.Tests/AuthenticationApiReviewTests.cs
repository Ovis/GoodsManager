using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoodsManager.Web.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static GoodsManager.Tests.AuthenticationTests;

namespace GoodsManager.Tests;

/// <summary>標準IdentityのETagと、アカウント・パスキー編集の競合を検証</summary>
public sealed class AuthenticationApiReviewTests
{
    /// <summary>ログインIDの古い編集や版なしの保存を拒否</summary>
    [TestCase(null, 428)]
    [TestCase("*", 412)]
    [TestCase("\"stale\"", 412)]
    public async Task AccountRequiresCurrentExplicitVersion(string? etag, int status)
    {
        await using var app = new TestApplication();
        using var client = Client(app);
        await SetupAsync(client);
        var response = await SendAsync(client, "account", new { loginId = "changed" }, HttpMethod.Put, etag);
        Assert.That((int)response.StatusCode, Is.EqualTo(status));
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.That((await db.Users.SingleAsync()).UserName, Is.EqualTo("owner"));
        Assert.That((await db.SystemStates.SingleAsync()).DataRevision, Is.EqualTo(2)); // 初期カテゴリ投入 + アカウント作成
    }

    /// <summary>同じ版からの同時編集は一方だけ保存し、ログイン期限を延長しない</summary>
    [Test]
    public async Task ConcurrentAccountEditsCommitOnceAndPreserveSessionExpiry()
    {
        await using var app = new TestApplication();
        using var first = Client(app);
        await SetupAsync(first);
        using var second = Client(app);
        await SendAsync(second, "auth/password", new { loginId = "owner", password = Password, rememberMe = false });
        var session = await first.GetAsync("/api/v1/auth/session");
        var version = session.Headers.ETag!.ToString();
        var expires = (await session.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("expiresAt").GetString();
        var responses = await Task.WhenAll(
            SendAsync(first, "account", new { loginId = "owner-first" }, HttpMethod.Put, version),
            SendAsync(second, "account", new { loginId = "owner-second" }, HttpMethod.Put, version));
        Assert.That(responses.Select(response => response.StatusCode), Is.EquivalentTo(new[] { HttpStatusCode.OK, HttpStatusCode.PreconditionFailed }));
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.SingleAsync();
        Assert.That(user.UserName, Is.AnyOf("owner-first", "owner-second"));
        Assert.That((await db.SystemStates.SingleAsync()).DataRevision, Is.EqualTo(3));
        var after = await first.GetFromJsonAsync<JsonElement>("/api/v1/auth/session");
        Assert.That(after.GetProperty("expiresAt").GetString(), Is.EqualTo(expires));
        Assert.That(after.GetProperty("user").GetProperty("loginId").GetString(), Is.EqualTo(user.UserName));
        using var login = Client(app);
        Assert.That((await SendAsync(login, "auth/password", new { loginId = user.UserName!.ToUpperInvariant(), password = Password, rememberMe = false })).StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    /// <summary>無変更の編集や不正値で、変更番号と版を不要に更新しない</summary>
    [Test]
    public async Task NoOpAndInvalidAccountEditsLeaveVersionUnchanged()
    {
        await using var app = new TestApplication();
        using var client = Client(app);
        await SetupAsync(client);
        var session = await client.GetAsync("/api/v1/auth/session");
        var version = session.Headers.ETag!.ToString();
        var unchanged = await SendAsync(client, "account", new { loginId = "owner" }, HttpMethod.Put, version);
        Assert.That(unchanged.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(unchanged.Headers.ETag!.ToString(), Is.EqualTo(version));
        foreach (var name in new[] { "name\0", new string('a', 65), "日本語" })
        {
            Assert.That((await SendAsync(client, "account", new { loginId = name }, HttpMethod.Put, version)).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        }

        await using var scope = app.Services.CreateAsyncScope();
        Assert.That((await scope.ServiceProvider.GetRequiredService<AppDbContext>().SystemStates.SingleAsync()).DataRevision, Is.EqualTo(2));
    }

    /// <summary>パスキーの改名・削除に版を要求し、拒否時は情報を変更しない</summary>
    [TestCase(false, null, 428)]
    [TestCase(true, null, 428)]
    [TestCase(false, "\"stale\"", 412)]
    [TestCase(true, "\"stale\"", 412)]
    public async Task PasskeyManagementRejectsMissingAndStaleVersions(bool delete, string? version, int status)
    {
        await using var app = new TestApplication();
        using var client = Client(app);
        await SetupAsync(client);
        using var authenticator = new TestAuthenticator();
        await RegisterAsync(client, authenticator);
        var response = await SendAsync(client, "account/passkeys/" + authenticator.Id,
            delete ? (object)new { password = Password } : new { displayName = "変更" }, delete ? HttpMethod.Delete : HttpMethod.Put, version);
        Assert.That((int)response.StatusCode, Is.EqualTo(status));
        var rows = await client.GetFromJsonAsync<JsonElement>("/api/v1/account/passkeys");
        Assert.That(rows.GetArrayLength(), Is.EqualTo(1));
        Assert.That(rows[0].GetProperty("displayName").GetString(), Is.EqualTo("元の名前"));
    }

    /// <summary>同時改名と古い削除を拒否し、更新済みのパスキーを消さない</summary>
    [Test]
    public async Task ConcurrentPasskeyEditsAndStaleDeletionDoNotOverwrite()
    {
        await using var app = new TestApplication();
        using var first = Client(app);
        await SetupAsync(first);
        using var authenticator = new TestAuthenticator();
        await RegisterAsync(first, authenticator);
        using var second = Client(app);
        await SendAsync(second, "auth/password", new { loginId = "owner", password = Password, rememberMe = false });
        var version = await PasskeyTests.EtagAsync(first, authenticator.Id);
        var responses = await Task.WhenAll(
            SendAsync(first, "account/passkeys/" + authenticator.Id, new { displayName = "名前1" }, HttpMethod.Put, version),
            SendAsync(second, "account/passkeys/" + authenticator.Id, new { displayName = "名前2" }, HttpMethod.Put, version));
        Assert.That(responses.Select(response => response.StatusCode), Is.EquivalentTo(new[] { HttpStatusCode.OK, HttpStatusCode.PreconditionFailed }));
        Assert.That((await SendAsync(second, "account/passkeys/" + authenticator.Id, new { password = Password }, HttpMethod.Delete, version)).StatusCode, Is.EqualTo(HttpStatusCode.PreconditionFailed));
        Assert.That((await SendAsync(second, "account/passkeys/" + authenticator.Id, new { password = Password }, HttpMethod.Delete, await PasskeyTests.EtagAsync(second, authenticator.Id))).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.That(await db.UserPasskeys.CountAsync(), Is.Zero);
        Assert.That((await db.SystemStates.SingleAsync()).DataRevision, Is.EqualTo(5));
    }

    /// <summary>変更番号の更新失敗で、先に保存したログインIDと標準スタンプも取り消す</summary>
    [Test]
    public async Task AccountEditRollsBackWhenRevisionCannotAdvance()
    {
        await using var app = new TestApplication();
        using var client = Client(app);
        await SetupAsync(client);
        var version = (await client.GetAsync("/api/v1/auth/session")).Headers.ETag!.ToString();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.ExecuteSqlRawAsync("UPDATE SystemState SET DataRevision=9223372036854775807");
        }

        Assert.That((await SendAsync(client, "account", new { loginId = "changed" }, HttpMethod.Put, version)).StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
        var after = await client.GetAsync("/api/v1/auth/session");
        Assert.That(after.Headers.ETag!.ToString(), Is.EqualTo(version));
        Assert.That((await after.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("user").GetProperty("loginId").GetString(), Is.EqualTo("owner"));
    }

    /// <summary>標準ハンドラーで試験用認証器を登録</summary>
    private static async Task RegisterAsync(HttpClient client, TestAuthenticator authenticator)
    {
        var response = await SendAsync(client, "account/passkeys/options", new { verification = new { password = Password } });
        var options = await response.Content.ReadFromJsonAsync<JsonElement>();
        response = await SendAsync(client, "account/passkeys", new { challengeId = options.GetProperty("challengeId"), credential = authenticator.Create(options.GetProperty("publicKeyOptions")), displayName = "元の名前" });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
    }
}
