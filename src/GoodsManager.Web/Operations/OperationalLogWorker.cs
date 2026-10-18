namespace GoodsManager.Web.Operations;

/// <summary>操作がない日も日付によるログ保持を処理する</summary>
public sealed class OperationalLogWorker(OperationalLogging logs) : BackgroundService
{
    /// <summary>定期記録で日次整理と書込障害の再確認を行う</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        var logger = logs.CreateLogger("GoodsManager.Web.Operations");
        do { logger.LogInformation(new EventId(1001), "ログ保全 Code={Code}", "LOG_MAINTENANCE"); }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
