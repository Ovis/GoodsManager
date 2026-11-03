using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace GoodsManager.Tests;

/// <summary>試験ごとに実ファイルDBと鍵の保存先を分離</summary>
public sealed class TestApplication : WebApplicationFactory<Program>
{
    /// <summary>他の試験や稼働データと共有しない、DB・鍵・ログの保存先。</summary>
    public string DataDirectory
    {
        get;
    }

    /// <summary>再起動試験の次のホストへ渡すため、一時領域を終了時に削除しない。</summary>
    public bool PreserveData
    {
        get; set;
    }

    /// <summary>標準パスキー検証のRPとOriginを起動前に指定する試験用の設定。</summary>
    public string? AuthenticationOrigin
    {
        get; init;
    }

    /// <summary>認証期限を実時間から独立して進める試験時計。</summary>
    public TestClock Clock { get; } = new();

    /// <summary>本番のサービス登録後に、隔離試験に必要な置換だけを適用する。</summary>
    public Action<IServiceCollection>? ConfigureTestServices
    {
        get; init;
    }

    /// <summary>再起動試験だけ既存の一時領域を再利用</summary>
    public TestApplication(string? dataDirectory = null)
    {
        DataDirectory = dataDirectory ?? Path.Combine(Path.GetTempPath(), "GoodsManager.Tests", Guid.NewGuid().ToString("N"));
    }

    /// <summary>認証構成はProgramのサービス登録時に読むため、起動前のホスト構成で指定</summary>
    protected override IHost CreateHost(IHostBuilder builder)
    {
        if (AuthenticationOrigin is not null)
        {
            builder.ConfigureHostConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Authentication:Origin"] = AuthenticationOrigin }));
        }

        return base.CreateHost(builder);
    }

    /// <summary>他の試験や開発用DBを変更しない構成</summary>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:Directory"] = DataDirectory, ["Backups:WorkerEnabled"] = "false", ["Operations:HealthPort"] = "0" }));
        builder.ConfigureServices(services =>
        {
            // 商品準備用の入口は試験アセンブリからだけ登録し、配布アプリには含めない。
            services.AddControllers().AddApplicationPart(typeof(ProductFixtureController).Assembly);
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            ConfigureTestServices?.Invoke(services);
        });
    }

    /// <summary>公開TestServerとは別の内部専用HTTPリスナーで準備検査を呼ぶ。</summary>
    public async Task<HttpResponseMessage> ReadinessAsync()
    {
        using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false });
        return await client.GetAsync(Services.GetRequiredService<GoodsManager.Web.Operations.InternalHealthListener>().ReadinessUri);
    }

    /// <summary>ホストと接続プールを解放した後、試験用ファイルを削除</summary>
    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (!PreserveData && Directory.Exists(DataDirectory))
        {
            Directory.Delete(DataDirectory, recursive: true);
        }
    }
}

/// <summary>実時間を待たず絶対期限の境界を検証</summary>
public sealed class TestClock : TimeProvider
{
    private DateTimeOffset now = DateTimeOffset.UtcNow;

    /// <summary>試験用の現在時刻</summary>
    public override DateTimeOffset GetUtcNow() => now;

    /// <summary>制御した時間を進める</summary>
    public void Advance(TimeSpan value) => now += value;
}
