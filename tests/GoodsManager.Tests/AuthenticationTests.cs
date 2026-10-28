using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoodsManager.Web.Auth;
using GoodsManager.Web.Storage;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoodsManager.Tests;

/// <summary>実SQLiteとHTTPのクッキー・CSRF・失効・競合を検証</summary>
public sealed class AuthenticationTests
{
    public const string Password = "test-password-123";

    /// <summary>Secureクッキーを使う同一オリジンのクライアントを作成</summary>
    public static HttpClient Client(TestApplication app)
    {
        var client = app.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost:7443"), AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Collection-Id", GoodsManager.Web.Collections.CollectionSchema.DefaultId.ToString("D"));
        return client;
    }

    /// <summary>認証状態に対応したCSRFトークンを取得して送信</summary>
    public static async Task<HttpResponseMessage> SendAsync(HttpClient client, string path, object? body = null, HttpMethod? method = null, string? ifMatch = null)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
        using var request = new HttpRequestMessage(method ?? HttpMethod.Post, "/api/v1/" + path);
        request.Headers.Add("X-CSRF-Token", csrf.GetProperty("token").GetString());
        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }
        request.Content = JsonContent.Create(body ?? new { });
        return await client.SendAsync(request);
    }

    /// <summary>初期ユーザーを作成</summary>
    public static async Task<HttpResponseMessage> SetupAsync(HttpClient client) => await SendAsync(client, "setup", new { loginId = "owner", password = Password });

    /// <summary>未認証APIは401、匿名セットアップもCSRF必須</summary>
    [Test]
    public async Task AnonymousEndpointsEnforceCsrfAndReturn401()
    {
        await using var app = new TestApplication();
        using var client = Client(app);
        var session = await client.GetAsync("/api/v1/auth/session");
        Assert.That(session.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(session.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/problem+json"));
        var setup = await client.PostAsJsonAsync("/api/v1/setup", new { loginId = "owner", password = Password });
        Assert.That(setup.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That((await setup.Content.ReadAsStringAsync()), Does.Contain("CSRF_FAILED"));
        Assert.That((await client.GetFromJsonAsync<JsonElement>("/api/v1/setup/status")).GetProperty("setupRequired").GetBoolean(), Is.True);
    }

    /// <summary>同時セットアップの一方だけ確定し、単一制約も直接SQLで有効</summary>
    [Test]
    public async Task ConcurrentSetupCreatesOnlyOneAccount()
    {
        await using var app = new TestApplication();
        using var first = Client(app);
        using var second = Client(app);
        var results = await Task.WhenAll(SetupAsync(first), SetupAsync(second));
        Assert.That(results.Select(x => x.StatusCode), Is.EquivalentTo(new[] { HttpStatusCode.Created, HttpStatusCode.Conflict }));
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.That(await db.Users.CountAsync(), Is.EqualTo(1));
        Assert.That((await db.SystemStates.SingleAsync()).DataRevision, Is.EqualTo(1)); // 初期カテゴリはスキーマの初期データ。アカウント作成で1回増える
    }

    /// <summary>初期登録だけでセッションが発行され、登録済みなら別アカウントを作成できない</summary>
    [Test]
    public async Task SetupStartsSessionAndClosesRegistration()
    {
        await using var app = new TestApplication();
        using var client = Client(app);
        Assert.That((await SetupAsync(client)).StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var session = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/session");
        Assert.That(session.GetProperty("user").GetProperty("loginId").GetString(), Is.EqualTo("owner"));
        Assert.That((await client.GetFromJsonAsync<JsonElement>("/api/v1/setup/status")).GetProperty("setupRequired").GetBoolean(), Is.False);
        using var other = Client(app);
        var repeated = await SendAsync(other, "setup", new { loginId = "another", password = Password });
        Assert.That(repeated.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That((await repeated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString(), Is.EqualTo("SETUP_COMPLETED"));
        Assert.That((await other.GetAsync("/api/v1/auth/session")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That((await client.GetAsync("/api/v1/auth/session")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }
    /// <summary>セッションクッキーと30日保持の差、無操作・操作時の絶対期限</summary>
    [TestCase(false, 7)]
    [TestCase(true, 30)]
    public async Task CookieHasAbsoluteExpiryAndNeverSlides(bool rememberMe, int days)
    {
        await using var app = new TestApplication();
        using var setup = Client(app);
        Assert.That((await SetupAsync(setup)).StatusCode, Is.EqualTo(HttpStatusCode.Created));
        using var client = Client(app);
        var login = await SendAsync(client, "auth/password", new { loginId = "OWNER", password = Password, rememberMe });
        Assert.That(login.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var cookie = login.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("__Host-GoodsManager="));
        Assert.That(cookie.Contains("expires=", StringComparison.OrdinalIgnoreCase), Is.EqualTo(rememberMe));
        Assert.That(cookie, Does.Contain("secure").And.Contain("httponly"));
        app.Clock.Advance(TimeSpan.FromDays(days) - TimeSpan.FromSeconds(1));
        var before = await client.GetAsync("/api/v1/auth/session");
        Assert.That(before.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(before.Headers.TryGetValues("Set-Cookie", out _), Is.False);
        app.Clock.Advance(TimeSpan.FromSeconds(2));
        Assert.That((await client.GetAsync("/api/v1/auth/session")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    /// <summary>ログアウト前のクッキーをコピーしても再利用できない</summary>
    [Test]
    public async Task LogoutDeletesServerTicket()
    {
        await using var app = new TestApplication();
        using var client = Client(app);
        var setup = await SetupAsync(client);
        var cookie = setup.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("__Host-GoodsManager=")).Split(';')[0];
        Assert.That((await SendAsync(client, "auth/logout")).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        using var replay = Client(app);
        replay.DefaultRequestHeaders.Add("Cookie", cookie);
        Assert.That((await replay.GetAsync("/api/v1/auth/session")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    /// <summary>本人確認失敗は変更を残さず、成功時は別端末も即時失効</summary>
    [Test]
    public async Task PasswordChangeRequiresVerificationAndRevokesEveryDevice()
    {
        await using var app = new TestApplication();
        using var first = Client(app);
        await SetupAsync(first);
        using var second = Client(app);
        await SendAsync(second, "auth/password", new { loginId = "owner", password = Password, rememberMe = true });
        var invalid = await SendAsync(first, "account/password", new { newPassword = "new-password-123", verification = new { password = "wrong-password" } });
        Assert.That(invalid.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That((await second.GetAsync("/api/v1/auth/session")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var changed = await SendAsync(first, "account/password", new { newPassword = "new-password-123", verification = new { password = Password } });
        Assert.That(changed.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That((await second.GetAsync("/api/v1/auth/session")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That((await first.GetAsync("/api/v1/auth/session")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        var login = await SendAsync(second, "auth/password", new { loginId = "owner", password = "new-password-123", rememberMe = false });
        Assert.That(login.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    /// <summary>失敗5回で標準ロックアウトし、IDの有無は同じ応答</summary>
    [Test]
    public async Task FailedPasswordsLockAccount()
    {
        await using var app = new TestApplication();
        using var client = Client(app);
        await SetupAsync(client);
        for (var i = 0; i < 5; i++)
        {
            Assert.That((await SendAsync(client, "auth/password", new { loginId = "owner", password = "wrong-password", rememberMe = false })).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        }

        Assert.That((await SendAsync(client, "auth/password", new { loginId = "owner", password = Password, rememberMe = false })).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        await using var scope = app.Services.CreateAsyncScope();
        var user = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.SingleAsync();
        Assert.That(user.LockoutEnd, Is.GreaterThan(DateTimeOffset.UtcNow));
        Assert.That((await SendAsync(client, "auth/password", new { loginId = "missing", password = Password, rememberMe = false })).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    /// <summary>復旧失敗は保全番号を変更せず、成功時に全端末を失効</summary>
    [Test]
    public async Task RecoveryUsesStandardIdentityAndIsAtomic()
    {
        await using var app = new TestApplication();
        using var client = Client(app);
        await SetupAsync(client);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var recovery = scope.ServiceProvider.GetRequiredService<AccountRecovery>();
            Assert.That((await recovery.ResetAsync("short", false)).Succeeded, Is.False);
        }

        Assert.That((await client.GetAsync("/api/v1/auth/session")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        await using (var scope = app.Services.CreateAsyncScope())
        {
            Assert.That((await scope.ServiceProvider.GetRequiredService<AccountRecovery>().ResetAsync("recovered-password", false)).Succeeded, Is.True);
            Assert.That((await scope.ServiceProvider.GetRequiredService<AppDbContext>().SystemStates.SingleAsync()).DataRevision, Is.EqualTo(2));
        }

        Assert.That((await client.GetAsync("/api/v1/auth/session")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That((await SendAsync(client, "auth/password", new { loginId = "owner", password = "recovered-password", rememberMe = false })).StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    /// <summary>文字種を強制せず、絵文字のコードポイント数とNULを検証</summary>
    [Test]
    public async Task PasswordPolicyCountsCodePoints()
    {
        var validator = new CodePointPasswordValidator();
        Assert.That((await validator.ValidateAsync(null!, new AppUser(), string.Concat(Enumerable.Repeat("😀", 12)))).Succeeded, Is.True);
        Assert.That((await validator.ValidateAsync(null!, new AppUser(), string.Concat(Enumerable.Repeat("😀", 11)))).Succeeded, Is.False);
        Assert.That((await validator.ValidateAsync(null!, new AppUser(), new string('a', 129))).Succeeded, Is.False);
        Assert.That((await validator.ValidateAsync(null!, new AppUser(), "valid-password\0")).Succeeded, Is.False);
    }

    /// <summary>再起動後も永続鍵・DBの標準チケットを復元できる</summary>
    [Test]
    public async Task PersistentTicketSurvivesHostRestart()
    {
        string directory;
        string cookie;
        await using (var first = new TestApplication { PreserveData = true })
        {
            directory = first.DataDirectory;
            using var client = Client(first);
            await SetupAsync(client);
            var response = await SendAsync(client, "auth/password", new { loginId = "owner", password = Password, rememberMe = true });
            cookie = response.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("__Host-GoodsManager=")).Split(';')[0];
        }

        await using var second = new TestApplication(directory);
        using var restored = Client(second);
        restored.DefaultRequestHeaders.Add("Cookie", cookie);
        Assert.That((await restored.GetAsync("/api/v1/auth/session")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }
}
