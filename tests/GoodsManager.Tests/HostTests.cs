using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace GoodsManager.Tests;

/// <summary>公開ヘルス確認とAPIの応答形式を検証</summary>
public sealed class HostTests
{
    /// <summary>アプリが起動し、生存確認を返す</summary>
    [Test]
    public async Task LiveEndpointStartsWithoutFrontend()
    {
        await using var factory = new TestApplication();
        using var client = factory.CreateClient();
        Assert.That((await client.GetAsync("/health/live")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await client.GetAsync("/health/ready")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    /// <summary>各画面のURLを直接開いても認証前にキャッシュ不可のSPAを返す</summary>
    [TestCase("/setup")]
    [TestCase("/account")]
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
