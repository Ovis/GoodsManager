using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoodsManager.Web.Development;
using GoodsManager.Web.Masters;
using GoodsManager.Web.Products;
using GoodsManager.Web.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using static GoodsManager.Tests.AuthenticationTests;

namespace GoodsManager.Tests;

public sealed class ProductBoundaryTests
{
    [Test]
    public void TotalsUseInt64AndRejectUnsafeNumbersAndOverflow()
    {
        Assert.That(QuantityTotals.Calculate([int.MaxValue, int.MaxValue], 1), Is.EqualTo(4294967295L));
        Assert.That(QuantityTotals.Calculate([], StorageValues.MaximumSafeInteger), Is.EqualTo(StorageValues.MaximumSafeInteger));
        foreach (var itemCount in new[] { StorageValues.MaximumSafeInteger + 1, long.MaxValue })
            Assert.That(Assert.Throws<MasterProblem>(() => QuantityTotals.Calculate([1], itemCount))!.Code, Is.EqualTo("QUANTITY_LIMIT_EXCEEDED"));
        Assert.Throws<MasterProblem>(() => QuantityTotals.Calculate(Enumerable.Repeat(int.MaxValue, 4194305), 0));
        Assert.Throws<InvalidDataException>(() => QuantityTotals.Calculate([0], 0));
    }

    [TestCase("Development", HttpStatusCode.OK)]
    [TestCase("Staging", HttpStatusCode.NotFound)]
    [TestCase("Production", HttpStatusCode.NotFound)]
    public async Task BrowserResourcesAreDevelopmentOnly(string environment, HttpStatusCode expected)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.WebHost.UseTestServer();
        await using var app = builder.Build();
        app.MapProductCheck();
        await app.StartAsync();
        using var client = app.GetTestClient();
        foreach (var path in new[] { "/dev/product-check", "/dev/product-check/product-check.js" })
        {
            var response = await client.GetAsync(path);
            Assert.That(response.StatusCode, Is.EqualTo(expected));
            if (expected == HttpStatusCode.OK)
            {
                Assert.That(response.Headers.CacheControl!.NoStore, Is.True);
                Assert.That(response.Headers.GetValues("Content-Security-Policy").Single(), Does.Contain("script-src 'self'").And.Contain("frame-ancestors 'none'"));
            }
        }
    }

    [TestCase("Staging")]
    [TestCase("Production")]
    public void NonDevelopmentRejectsFixtureBeforeCallingTheWriteService(string environment)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        var controller = new DevelopmentProductsController(builder.Environment, null!, null!);
        var error = Assert.Throws<MasterProblem>(() => controller.Create());
        Assert.That(error!.Status, Is.EqualTo(404));
    }
}
