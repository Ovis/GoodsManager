using System.Net;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace GoodsManager.Web.Operations;

/// <summary>公開アプリとは別のループバック専用リスナーでだけ、詳細検査を実行する。</summary>
public sealed class InternalHealthListener(IServiceProvider services, IConfiguration configuration, IHostEnvironment environment) : IHostedService, IAsyncDisposable
{
    private WebApplication? server;

    /// <summary>起動後に確定する内部検査URL。動的ポートの試験でも公開リスナーを経由しない。</summary>
    public Uri? ReadinessUri
    {
        get; private set;
    }

    /// <summary>公開ホストの待受設定を引き継がず、ループバックに詳細検査専用サーバーを起動する。</summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var port = configuration.GetValue("Operations:HealthPort", 8081);
        if (port is < 0 or > 65535)
        {
            throw new InvalidOperationException("内部ヘルス確認ポートが範囲外");
        }

        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [], EnvironmentName = environment.EnvironmentName, ContentRootPath = environment.ContentRootPath });
        // 公開リスナーのURL・環境変数・Kestrel設定を引き継いで外部へバインドしない。
        builder.Configuration.Sources.Clear();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, port, endpoint => endpoint.Protocols = HttpProtocols.Http1));
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(services.GetRequiredService<OperationalLogging>());

        server = builder.Build();
        server.MapGet("/health/ready", async (HttpContext context) =>
        {
            try
            {
                var report = await services.GetRequiredService<HealthCheckService>().CheckHealthAsync(check => check.Tags.Contains("ready"), context.RequestAborted);
                return Results.Text(report.Status == HealthStatus.Healthy ? "Healthy" : "Unhealthy", statusCode: report.Status == HealthStatus.Healthy ? 200 : 503);
            }
            catch (Exception)
            {
                return Results.Text("Unhealthy", statusCode: 503);
            }
        });

        await server.StartAsync(cancellationToken);
        ReadinessUri = new Uri(server.Urls.Single().TrimEnd('/') + "/health/ready");
    }

    /// <summary>内部検査サーバーの受け付けをホスト終了に合わせて停止する。</summary>
    public Task StopAsync(CancellationToken cancellationToken) => server?.StopAsync(cancellationToken) ?? Task.CompletedTask;

    /// <summary>所有権を一度だけ取り出し、内部サーバーのリソースを解放する。</summary>
    public async ValueTask DisposeAsync()
    {
        var current = Interlocked.Exchange(ref server, null);
        if (current is not null)
        {
            await current.DisposeAsync();
        }
    }
}
