using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using GoodsManager.Web.Auth;
using GoodsManager.Web.Masters;
using GoodsManager.Web.Products;

namespace GoodsManager.Web.Storage;

/// <summary>現在の工程で必要な永続データ</summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityUserContext<AppUser, Guid>(options)
{
    public DbSet<SystemState> SystemStates => Set<SystemState>();
    public DbSet<AuthTicket> AuthTickets => Set<AuthTicket>();

    /// <summary>通常テーブルへ制約を明示</summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        // 設計時ツールや試験でも、DIの有無に左右されず標準パスキースキーマを使用
        modelBuilder.Entity<IdentityUserPasskey<Guid>>(entity =>
        {
            entity.HasKey(x => x.CredentialId);
            entity.ToTable("AspNetUserPasskeys", table =>
            {
                table.HasCheckConstraint("CK_Passkey_Data", "typeof(Data)='text' AND json_valid(Data)=1 AND json_type(Data)='object'");
                table.HasCheckConstraint("CK_Passkey_CredentialId_Length", "length(CredentialId) BETWEEN 1 AND 1023");
                table.HasCheckConstraint("CK_Passkey_Name", "json_type(Data,'$.Name') IS 'text' AND length(json_extract(Data,'$.Name')) BETWEEN 1 AND 100 AND instr(json_extract(Data,'$.Name'),char(0))=0");
            });
            entity.Property(x => x.CredentialId).HasMaxLength(1024);
            entity.OwnsOne(x => x.Data).ToJson();
            entity.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId);
        });
        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.HasIndex(x => x.Singleton).IsUnique();
            entity.Property(x => x.UserName).IsRequired();
            entity.Property(x => x.NormalizedUserName).IsRequired();
            entity.Property(x => x.PasswordHash).IsRequired();
            entity.Property(x => x.SecurityStamp).IsRequired();
            entity.ToTable("AspNetUsers", table =>
            {
                table.HasCheckConstraint("CK_User_Singleton", "Singleton=1");
                table.HasCheckConstraint("CK_User_Name", "length(UserName) BETWEEN 1 AND 64 AND UserName NOT GLOB '*[^a-zA-Z0-9._-]*'");
                table.HasCheckConstraint("CK_User_Failures", "AccessFailedCount>=0");
            });
        });
        modelBuilder.Entity<AuthTicket>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => x.ExpiresAt);
        });
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
        MasterSchema.Configure(modelBuilder);
        ProductSchema.Configure(modelBuilder);
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
