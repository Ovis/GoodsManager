using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace GoodsManager.Web.Storage;

/// <summary>外部へ詳細を公開しない準備状態確認</summary>
public sealed class DatabaseHealthCheck(AppDbContext database) : IHealthCheck
{
    /// <summary>移行状態と主要な1行の読取を確認</summary>
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            return !await DatabaseMaintenance.HasPendingMigrationsAsync(database, cancellationToken)
                && await database.SystemStates.AnyAsync(x => x.Id == 1, cancellationToken)
                && !await database.Set<GoodsManager.Web.Images.SystemAlert>().AnyAsync(x => x.ResolvedAt == null && x.Code == "IMAGE_FILE_UNAVAILABLE", cancellationToken)
                ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy();
        }
        catch (Exception)
        {
            return HealthCheckResult.Unhealthy();
        }
    }
}
