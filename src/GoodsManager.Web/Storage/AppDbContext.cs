using Microsoft.EntityFrameworkCore;

namespace GoodsManager.Web.Storage;

/// <summary>現在の工程で必要な永続データ</summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<SystemState> SystemStates => Set<SystemState>();

    /// <summary>通常テーブルへ制約を明示</summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<SystemState>(entity =>
        {
            entity.ToTable("SystemState", table =>
            {
                table.HasCheckConstraint("CK_SystemState_Singleton", "\"Id\" = 1");
                table.HasCheckConstraint("CK_SystemState_DataRevision", "\"DataRevision\" >= 0");
            });
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.HasData(new SystemState { Id = 1, DataRevision = 0, UpdatedAt = DateTimeOffset.UnixEpoch });
        });
        StorageValues.ApplyConventions(modelBuilder);
    }
}

/// <summary>保全対象データの変更番号。運用履歴だけの更新では増やさない</summary>
public sealed class SystemState
{
    public int Id { get; set; } = 1;
    public long DataRevision { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
