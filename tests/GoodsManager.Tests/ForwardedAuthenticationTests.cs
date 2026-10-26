using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using static GoodsManager.Tests.AuthenticationTests;

namespace GoodsManager.Tests;

/// <summary>公開HTTPSから内部HTTPへ中継する本番構成で、Cookie・CSRF・固定RPの認証を検証</summary>
public sealed class ForwardedAuthenticationTests
{
    /// <summary>本番の固定オリジンを指定し、既存データから分離したアプリを起動</summary>
    private static WebApplicationFactory<Program> Production(TestApplication app) => app.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));

    /// <summary>ブラウザ相当のSecure Cookie処理の内側で、プロキシからアプリへのHTTP転送を再現</summary>
    private static HttpClient ThroughProxy(WebApplicationFactory<Program> app, bool forwardedHttps = true) =>
        app.CreateDefaultClient(new Uri("https://localhost:7443"), new CookieContainerHandler(), new InternalHttpProxy(forwardedHttps));

    /// <summary>HTTPS情報を認証より先に反映し、初期登録・再ログイン・パスキーログインを完了</summary>
    [Test]
    public async Task ForwardedHttpsSupportsSetupPasswordAndPasskeyAuthentication()
    {
        await using var app = new TestApplication { AuthenticationOrigin = "https://localhost:7443" };
        await using var production = Production(app);
        using var client = ThroughProxy(production);
        var csrfResponse = await client.GetAsync("/api/v1/auth/csrf");
        Assert.That(csrfResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(csrfResponse.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("__Host-GoodsManager-Csrf=")), Does.Contain("secure").And.Contain("httponly"));
        var anonymousToken = (await csrfResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
        var missingCsrf = await client.PostAsJsonAsync("/api/v1/setup", new { loginId = "owner", password = Password });
        Assert.That(missingCsrf.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That(await missingCsrf.Content.ReadAsStringAsync(), Does.Contain("CSRF_FAILED"));

        var setup = await SetupAsync(client);
        Assert.That(setup.StatusCode, Is.EqualTo(HttpStatusCode.Created), await setup.Content.ReadAsStringAsync());
        Assert.That(setup.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("__Host-GoodsManager=")), Does.Contain("secure").And.Contain("httponly"));
        Assert.That((await client.GetFromJsonAsync<JsonElement>("/api/v1/setup/status")).GetProperty("setupRequired").GetBoolean(), Is.False);
        Assert.That((await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/session")).GetProperty("user").GetProperty("loginId").GetString(), Is.EqualTo("owner"));

        using var staleToken = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout") { Content = JsonContent.Create(new { }) };
        staleToken.Headers.Add("X-CSRF-Token", anonymousToken);
        Assert.That((await client.SendAsync(staleToken)).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That((await SendAsync(client, "auth/logout")).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That((await client.GetAsync("/api/v1/auth/session")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That((await SendAsync(client, "auth/password", new { loginId = "OWNER", password = Password, rememberMe = false })).StatusCode, Is.EqualTo(HttpStatusCode.OK));

        using var authenticator = new TestAuthenticator();
        var optionsResponse = await SendAsync(client, "account/passkeys/options", new { verification = new { password = Password } });
        Assert.That(optionsResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var options = await optionsResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.That(options.GetProperty("publicKeyOptions").GetProperty("rp").GetProperty("id").GetString(), Is.EqualTo("localhost"));
        var registered = await SendAsync(client, "account/passkeys", new
        {
            challengeId = options.GetProperty("challengeId"),
            credential = authenticator.Create(options.GetProperty("publicKeyOptions")),
            displayName = "中継確認用"
        });
        Assert.That(registered.StatusCode, Is.EqualTo(HttpStatusCode.Created), await registered.Content.ReadAsStringAsync());
        Assert.That((await SendAsync(client, "auth/logout")).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        optionsResponse = await SendAsync(client, "auth/passkey/options", new { rememberMe = false });
        Assert.That(optionsResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        options = await optionsResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.That(options.GetProperty("publicKeyOptions").GetProperty("rpId").GetString(), Is.EqualTo("localhost"));
        var login = await SendAsync(client, "auth/passkey/verify", new
        {
            challengeId = options.GetProperty("challengeId"),
            credential = authenticator.Get(options.GetProperty("publicKeyOptions"))
        });
        Assert.That(login.StatusCode, Is.EqualTo(HttpStatusCode.OK), await login.Content.ReadAsStringAsync());
        Assert.That((await client.GetAsync("/api/v1/auth/session")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    /// <summary>公開Hostの一律検査を外しても、パスキーのOrigin・RP不一致は拒否</summary>
    [TestCase("origin")]
    [TestCase("rp")]
    public async Task ForwardedHttpsKeepsPasskeyOriginAndRpValidation(string invalid)
    {
        await using var app = new TestApplication { AuthenticationOrigin = "https://localhost:7443" };
        await using var production = Production(app);
        using var client = ThroughProxy(production);
        Assert.That((await SetupAsync(client)).StatusCode, Is.EqualTo(HttpStatusCode.Created));
        using var authenticator = new TestAuthenticator();
        var optionsResponse = await SendAsync(client, "account/passkeys/options", new { verification = new { password = Password } });
        var options = await optionsResponse.Content.ReadFromJsonAsync<JsonElement>();
        var response = await SendAsync(client, "account/passkeys", new
        {
            challengeId = options.GetProperty("challengeId"),
            credential = authenticator.Create(options.GetProperty("publicKeyOptions"),
                origin: invalid == "origin" ? "https://other.example.invalid" : "https://localhost:7443",
                rpId: invalid == "rp" ? "other.example.invalid" : "localhost"),
            displayName = "不正"
        });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.That(problem.GetProperty("code").GetString(), Is.EqualTo("PASSKEY_INVALID"));
        Assert.That(problem.TryGetProperty("reason", out _), Is.False);
        Assert.That((await client.GetFromJsonAsync<JsonElement>("/api/v1/account/passkeys")).GetArrayLength(), Is.Zero);
    }

    /// <summary>HTTPS情報のない内部HTTPではCSRF Cookieを発行せず、Secure属性を緩和しない</summary>
    [Test]
    public async Task MissingForwardedHttpsCannotIssueInsecureCsrfCookie()
    {
        await using var app = new TestApplication { AuthenticationOrigin = "https://localhost:7443" };
        await using var production = Production(app);
        using var client = ThroughProxy(production, forwardedHttps: false);
        Assert.That((await client.GetAsync("/api/v1/setup/status")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var response = await client.GetAsync("/api/v1/auth/csrf");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
        Assert.That((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString(), Is.EqualTo("INTERNAL_ERROR"));
        Assert.That(response.Headers.Contains("Set-Cookie"), Is.False);
    }

    /// <summary>公開Hostを保持したまま、認証用の外部HTTPS通信を内部HTTPへ中継</summary>
    private sealed class InternalHttpProxy(bool forwardedHttps) : DelegatingHandler
    {
        /// <summary>Cookieの送受信は外側の公開URLで扱い、サーバーへはHTTPとProtoだけを渡す</summary>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var original = request.RequestUri!;
            request.Headers.Host = original.Authority;
            request.RequestUri = new UriBuilder(original) { Scheme = "http", Host = "internal", Port = 8080 }.Uri;
            if (forwardedHttps) request.Headers.Add("X-Forwarded-Proto", "https");
            try
            {
                return await base.SendAsync(request, cancellationToken);
            }
            finally
            {
                request.RequestUri = original;
            }
        }
    }
}
