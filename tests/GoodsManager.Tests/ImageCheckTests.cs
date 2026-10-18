using System.Net;
using GoodsManager.Web.Development;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
namespace GoodsManager.Tests;

public sealed class ImageCheckTests
{
    [TestCase("Production")]
    [TestCase("Staging")]
    public async Task NonDevelopmentDoesNotExposeImageCheck(string environment)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment }); builder.WebHost.UseTestServer(); await using var app = builder.Build(); app.MapImageCheck(); await app.StartAsync(); using var client = app.GetTestClient();
        foreach (var path in new[] { "/dev/image-check", "/dev/image-check/image-check.js", "/dev/image-check/image-check.css" }) Assert.That((await client.GetAsync(path)).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
    [Test]
    public async Task DevelopmentServesOnlyKnownResourcesWithPrivatePolicy()
    {
        await using var app = new TestApplication(); using var client = app.CreateClient();
        foreach (var path in new[] { "/dev/image-check", "/dev/image-check/image-check.js", "/dev/image-check/image-check.css" }) { var response = await client.GetAsync(path); Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK)); Assert.That(response.Headers.CacheControl!.NoStore, Is.True); Assert.That(response.Headers.GetValues("Content-Security-Policy").Single(), Does.Contain("img-src 'self'").And.Contain("frame-ancestors 'none'")); }
        Assert.That((await client.GetAsync("/dev/image-check/appsettings.json")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
