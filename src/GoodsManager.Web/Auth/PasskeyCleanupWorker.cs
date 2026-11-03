namespace GoodsManager.Web.Auth;

/// <summary>匿名要求の有無にかかわらず認証待ち情報を期限で回収する。</summary>
public sealed class PasskeyCleanupWorker(PasskeyCeremonies ceremonies, TimeProvider clock) : BackgroundService
{
    /// <summary>30秒ごとに期限切れの認証待ち情報を回収し、新規要求に回収を依存させない。</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30), clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            ceremonies.PruneExpired();
        }
    }
}
