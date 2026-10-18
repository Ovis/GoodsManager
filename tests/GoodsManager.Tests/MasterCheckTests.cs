using System.Net;
using GoodsManager.Web.Development;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;

namespace GoodsManager.Tests;

public sealed class MasterCheckTests
{
    [TestCase("/dev/master-check", "text/html")]
    [TestCase("/dev/master-check/master-check.js", "text/javascript")]
    [TestCase("/dev/master-check/requests.mjs", "text/javascript")]
    [TestCase("/dev/master-check/master-check.css", "text/css")]
    public async Task DevelopmentServesOnlyKnownEmbeddedResources(string path, string contentType)
    {
        await using var factory = new TestApplication();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(path);
        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo(contentType));
            Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
            Assert.That(response.Headers.GetValues("Content-Security-Policy").Single(), Does.Contain("frame-ancestors 'none'").And.Contain("script-src 'self'"));
            Assert.That(response.Headers.GetValues("X-Content-Type-Options").Single(), Is.EqualTo("nosniff"));
        });
        Assert.That(await response.Content.ReadAsStringAsync(), Is.Not.Empty);
        Assert.That((await client.GetAsync("/api/v1/masters/works")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That((await client.GetAsync("/dev/master-check/appsettings.json")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await client.GetAsync("/Development/MasterCheck/index.html")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [TestCase("Production")]
    [TestCase("Staging")]
    public async Task NonDevelopmentNeverMapsVerificationResources(string environment)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.WebHost.UseTestServer();
        await using var app = builder.Build();
        app.MapMasterCheck();
        await app.StartAsync();
        using var client = app.GetTestClient();
        foreach (var path in new[] { "/dev/master-check", "/dev/master-check/master-check.js", "/dev/master-check/requests.mjs", "/dev/master-check/master-check.css" })
            Assert.That((await client.GetAsync(path)).StatusCode, Is.EqualTo(HttpStatusCode.NotFound), path);
    }
}
