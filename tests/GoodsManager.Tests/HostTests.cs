using System.Net;
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
