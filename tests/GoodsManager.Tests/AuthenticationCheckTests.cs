using System.Net;
using GoodsManager.Web.Development;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;

namespace GoodsManager.Tests;

/// <summary>検証画面の公開境界と配信ヘッダーを検証</summary>
public sealed class AuthenticationCheckTests
{
    /// <summary>実際のアプリから埋め込み資源を返し、認証APIを公開したままにする</summary>
    [TestCase("/dev/auth-check", "text/html")]
    [TestCase("/dev/auth-check/auth-check.js", "text/javascript")]
    [TestCase("/dev/auth-check/passkey-serialization.js", "text/javascript")]
    [TestCase("/dev/auth-check/auth-check.css", "text/css")]
    [TestCase("/dev/auth-check/config", "application/json")]
    public async Task DevelopmentServesOnlyKnownResources(string path, string contentType)
    {
        await using var factory = new TestApplication();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(path);
        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo(contentType));
            Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
            Assert.That(response.Headers.GetValues("Content-Security-Policy").Single(), Does.Contain("frame-ancestors 'none'"));
            Assert.That(response.Headers.GetValues("X-Content-Type-Options").Single(), Is.EqualTo("nosniff"));
        });
        Assert.That(await response.Content.ReadAsStringAsync(), Is.Not.Empty);
        Assert.That((await client.GetAsync("/api/v1/auth/session")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That((await client.GetAsync("/dev/auth-check/appsettings.json")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await client.GetAsync("/Development/AuthCheck/index.html")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    /// <summary>公開判定を本番とステージングの両方で確認</summary>
    [TestCase("Production")]
    [TestCase("Staging")]
    public async Task NonDevelopmentNeverMapsCheckResources(string environment)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.WebHost.UseTestServer();
        await using var app = builder.Build();
        app.MapAuthenticationCheck();
        await app.StartAsync();
        using var client = app.GetTestClient();
        foreach (var path in new[] { "/dev/auth-check", "/dev/auth-check/auth-check.js", "/dev/auth-check/passkey-serialization.js", "/dev/auth-check/auth-check.css", "/dev/auth-check/config" })
        {
            Assert.That((await client.GetAsync(path)).StatusCode, Is.EqualTo(HttpStatusCode.NotFound), path);
        }
    }
}
