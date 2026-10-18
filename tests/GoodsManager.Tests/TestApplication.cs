using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GoodsManager.Tests;

/// <summary>試験ごとに実ファイルDBと鍵の保存先を分離</summary>
public sealed class TestApplication : WebApplicationFactory<Program>
{
    public string DataDirectory { get; }
    public bool PreserveData { get; set; }
    public TestClock Clock { get; } = new();
    public Action<IServiceCollection>? ConfigureTestServices { get; init; }

    /// <summary>再起動試験だけ既存の一時領域を再利用</summary>
    public TestApplication(string? dataDirectory = null)
    {
        DataDirectory = dataDirectory ?? Path.Combine(Path.GetTempPath(), "GoodsManager.Tests", Guid.NewGuid().ToString("N"));
    }

    /// <summary>他の試験や開発用DBを変更しない構成</summary>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:Directory"] = DataDirectory }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            ConfigureTestServices?.Invoke(services);
        });
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
