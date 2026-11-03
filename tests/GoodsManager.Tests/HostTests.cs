using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace GoodsManager.Tests;

/// <summary>公開ヘルス確認とAPIの応答形式を検証</summary>
public sealed class HostTests
{
    /// <summary>直接HTMLと画面URLのGET・HEADに同じヘッダーを付け、アセットへHTML用CSPを広げない。</summary>
    [Test]
    public async Task HtmlHeadersAreConsistentForDirectFilesAndPagesWithoutChangingAssets()
    {
        await using var factory = new TestApplication();
        var webRoot = Path.Combine(factory.DataDirectory, "webroot");
        Directory.CreateDirectory(Path.Combine(webRoot, "assets"));
        await File.WriteAllTextAsync(Path.Combine(webRoot, "index.html"), "<!doctype html><title>Header test</title>");
        await File.WriteAllTextAsync(Path.Combine(webRoot, "assets", "app.js"), "console.log('asset');");
        await using var configured = factory.WithWebHostBuilder(builder => builder.UseWebRoot(webRoot));
        using var client = configured.CreateClient();
        string? policy = null;

        foreach (var method in new[] { HttpMethod.Get, HttpMethod.Head })
        {
            foreach (var path in new[] { "/index.html", "/collection" })
            {
                using var request = new HttpRequestMessage(method, path);
                using var response = await client.SendAsync(request);
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), method + " " + path);
                var current = response.Headers.GetValues("Content-Security-Policy").Single();
                policy ??= current;
                Assert.That(current, Is.EqualTo(policy).And.Contain("frame-ancestors 'none'"));
                Assert.That(response.Headers.GetValues("X-Content-Type-Options").Single(), Is.EqualTo("nosniff"));
                Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
                Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/html"));
                if (method == HttpMethod.Head)
                {
                    Assert.That(await response.Content.ReadAsByteArrayAsync(), Is.Empty);
                }
            }

            using var assetRequest = new HttpRequestMessage(method, "/assets/app.js");
            using var asset = await client.SendAsync(assetRequest);
            Assert.That(asset.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(asset.Headers.CacheControl?.Public, Is.True);
            Assert.That(asset.Headers.CacheControl?.MaxAge, Is.EqualTo(TimeSpan.FromDays(365)));
            Assert.That(asset.Headers.CacheControl?.Extensions.Select(x => x.Name), Does.Contain("immutable"));
            Assert.That(asset.Headers.Contains("Content-Security-Policy"), Is.False);
        }
    }

    /// <summary>アプリが起動し、生存確認を返す</summary>
    [Test]
    public async Task LiveEndpointStartsWithoutFrontend()
    {
        await using var factory = new TestApplication();
        using var client = factory.CreateClient();

        Assert.That((await client.GetAsync("/health/live")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await client.GetAsync("/health/ready")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await factory.ReadinessAsync()).StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    /// <summary>各画面のURLを直接開いても認証前にキャッシュ不可のSPAを返す</summary>
    [TestCase("/setup")]
    [TestCase("/account")]
    [TestCase("/settings")]
    [TestCase("/register")]
    [TestCase("/products/11111111-1111-4111-8111-111111111111")]
    public async Task CollectionPagesCanBeOpenedDirectly(string path)
    {
        await using var factory = new TestApplication();
        var webRoot = Path.Combine(factory.DataDirectory, "webroot");
        Directory.CreateDirectory(webRoot);
        await File.WriteAllTextAsync(Path.Combine(webRoot, "index.html"), "<!doctype html><title>Setup page</title>");

        await using var configured = factory.WithWebHostBuilder(builder => builder.UseWebRoot(webRoot));
        using var client = configured.CreateClient();

        var response = await client.GetAsync(path);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/html"));
        Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
        Assert.That(response.Headers.GetValues("Content-Security-Policy").Single(),
            Does.Contain("media-src 'self' blob:").And.Contain("script-src 'self';").And.Contain("frame-ancestors 'none'"));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("Setup page"));
    }

    /// <summary>未定義のAPIをHTMLへ転送しない</summary>
    [Test]
    public async Task MissingApiReturnsProblemDetails()
    {
        await using var factory = new TestApplication();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/missing");

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/problem+json"));
        });
    }
}
