using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace GoodsManager.Web.Storage;

/// <summary>外部へ詳細を公開しない準備状態確認</summary>
public sealed class DatabaseHealthCheck(AppDbContext database, DatabaseRuntime runtime, GoodsManager.Web.Backups.BackupOptions backups, GoodsManager.Web.Operations.OperationalLogging logs) : IHealthCheck
{
    /// <summary>主要領域への書込と移行状態・DB読取を確認</summary>
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            foreach (var directory in new[] { runtime.DirectoryPath, Path.Combine(runtime.DirectoryPath, "images"), Path.Combine(runtime.DirectoryPath, "keys"), backups.DirectoryPath, logs.DirectoryPath })
            {
                GoodsManager.Web.Backups.BackupPaths.RejectLinks(directory);
                var path = Path.Combine(directory, ".health-" + Guid.NewGuid().ToString("N"));
                using var probe = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose | FileOptions.WriteThrough);
                probe.WriteByte(0); probe.Flush(true);
            }
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
