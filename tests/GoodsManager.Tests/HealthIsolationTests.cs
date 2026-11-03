using System.Net;
using GoodsManager.Web.Operations;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GoodsManager.Tests;

/// <summary>重い詳細検査を公開リスナーから隔離し、内部ループバックでだけ実行することを検証する。</summary>
public sealed class HealthIsolationTests
{
    /// <summary>送信元やHostの偽装で詳細検査を公開できず、内部CLIだけが検査結果を取得できることを確認する。</summary>
    [TestCase("127.0.0.1", "127.0.0.1:8081")]
    [TestCase("192.0.2.1", "localhost:8081")]
    public async Task PublicListenerCannotRunReadyEvenWithSpoofedHeaders(string peer, string host)
    {
        await using var app = new TestApplication();
        using var client = app.CreateClient();
        // 重い検査なら失敗する状態にしても、公開側は検査結果を返さない。
        var images = Path.Combine(app.DataDirectory, "images");
        var held = images + ".health-test";
        Directory.Move(images, held);
        File.WriteAllText(images, "blocked");
        try
        {
            var response = await app.Server.SendAsync(context =>
            {
                context.Request.Path = "/health/ready";
                context.Request.Method = "GET";
                context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
                context.Request.Host = new HostString(host);
                context.Request.Headers["X-Forwarded-For"] = "127.0.0.1";
                context.Request.Headers["X-Forwarded-Host"] = "127.0.0.1:8081";
            });

            Assert.That(response.Response.StatusCode, Is.EqualTo(404));
            Assert.That((await client.GetAsync("/health/live")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That((await app.ReadinessAsync()).StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
        }
        finally
        {
            File.Delete(images);
            Directory.Move(held, images);
        }

        var internalUri = app.Services.GetRequiredService<InternalHealthListener>().ReadinessUri!;

        Assert.That(internalUri.Host, Is.EqualTo("127.0.0.1"));

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Operations:HealthPort"] = internalUri.Port.ToString() }).Build();

        Assert.That(await HealthCommand.RunAsync(config), Is.Zero);
    }
}
