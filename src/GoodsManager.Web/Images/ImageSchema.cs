using GoodsManager.Web.Auth;
using GoodsManager.Web.Products;
using Microsoft.EntityFrameworkCore;

namespace GoodsManager.Web.Images;

public sealed class ImageSettings
{
    public int Id { get; set; } = 1;
    public bool KeepOriginal { get; set; }
    public int DisplayMaxEdge { get; set; } = 2048;
    public int DisplayQuality { get; set; } = 82;
    public int ThumbMaxEdge { get; set; } = 320;
    public int ThumbQuality { get; set; } = 75;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Revision { get; set; } = 1;
}
public sealed class ImageFile
{
    public Guid Id { get; set; }
    public string Sha256 { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public string MediaType { get; set; } = "";
    public long ByteLength { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string State { get; set; } = "ready";
    public DateTimeOffset? DeleteAfter { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
public sealed class ImageAsset
{
    public Guid Id { get; set; }
    public string SourceSha256 { get; set; } = "";
    public string PolicyHash { get; set; } = "";
    public string PolicyJson { get; set; } = "";
    public bool KeepOriginal { get; set; }
    public Guid? OriginalFileId { get; set; }
    public Guid DisplayFileId { get; set; }
    public Guid ThumbnailFileId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
public sealed class ProductImage : IImageLink
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public Guid ImageAssetId { get; set; }
    public int SortOrder { get; set; }
    public bool IsPrimary { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
public sealed class InventoryItemImage : IImageLink
{
    public Guid Id { get; set; }
    public Guid InventoryItemId { get; set; }
    public Guid ImageAssetId { get; set; }
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
public sealed class TemporaryUpload
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid ClientUploadKey { get; set; }
    public string State { get; set; } = "pending";
    public string? StagingPath { get; set; }
    public string SourceSha256 { get; set; } = "";
    public long ByteLength { get; set; }
    public Guid? ImageAssetId { get; set; }
    public string PolicyJson { get; set; } = "";
    public string PolicyHash { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset ReplayUntil { get; set; }
    public string? ErrorCode { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
public sealed class SystemAlert
{
    public Guid Id { get; set; }
    public string AlertKey { get; set; } = "";
    public string Code { get; set; } = "";
    public string Severity { get; set; } = "warning";
    public string SafeDetailsJson { get; set; } = "{}";
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
}
public static class ImageSchema
{
    private static void Hash(Microsoft.EntityFrameworkCore.Metadata.Builders.TableBuilder table, string column) => table.HasCheckConstraint($"CK_{table.Metadata.GetTableName()}_{column}_Hash", $"length({column})=64 AND {column} NOT GLOB '*[^0-9a-f]*'");
    public static void Configure(ModelBuilder model)
    {
        model.Entity<ImageSettings>(e =>
        {
            e.ToTable("ImageSettings", t => { t.HasCheckConstraint("CK_ImageSettings_Singleton", "Id=1"); t.HasCheckConstraint("CK_ImageSettings_Values", "DisplayMaxEdge BETWEEN 1 AND 4096 AND ThumbMaxEdge BETWEEN 1 AND 1024 AND ThumbMaxEdge<=DisplayMaxEdge AND DisplayQuality BETWEEN 1 AND 100 AND ThumbQuality BETWEEN 1 AND 100 AND Revision>=1"); });
            e.HasKey(x => x.Id); e.Property(x => x.Id).ValueGeneratedNever(); e.Property(x => x.Revision).IsConcurrencyToken();
        });
        model.Entity<ImageFile>(e =>
        {
            e.ToTable("ImageFiles", t => { Hash(t, "Sha256"); t.HasCheckConstraint("CK_ImageFile_Values", "ByteLength>0 AND Width>0 AND Height>0 AND MediaType IN ('image/jpeg','image/png','image/webp') AND ((State='ready' AND DeleteAfter IS NULL) OR (State='deletePending' AND DeleteAfter IS NOT NULL))"); });
            e.HasKey(x => x.Id); e.Property(x => x.Id).HasColumnName("ImageFileId").ValueGeneratedNever(); e.HasIndex(x => x.Sha256).IsUnique(); e.HasIndex(x => x.RelativePath).IsUnique(); e.HasIndex(x => new { x.State, x.DeleteAfter });
        });
        model.Entity<ImageAsset>(e =>
        {
            e.ToTable("ImageAssets", t => { Hash(t, "SourceSha256"); Hash(t, "PolicyHash"); t.HasCheckConstraint("CK_ImageAsset_PolicyJson", "json_valid(PolicyJson)"); t.HasCheckConstraint("CK_ImageAsset_Original", "(KeepOriginal=1 AND OriginalFileId IS NOT NULL) OR (KeepOriginal=0 AND OriginalFileId IS NULL)"); });
            e.HasKey(x => x.Id); e.Property(x => x.Id).HasColumnName("ImageAssetId").ValueGeneratedNever(); e.HasIndex(x => new { x.SourceSha256, x.PolicyHash }).IsUnique();
            e.HasOne<ImageFile>().WithMany().HasForeignKey(x => x.OriginalFileId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ImageFile>().WithMany().HasForeignKey(x => x.DisplayFileId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ImageFile>().WithMany().HasForeignKey(x => x.ThumbnailFileId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<ProductImage>(e =>
        {
            e.ToTable("ProductImages", t => t.HasCheckConstraint("CK_ProductImage_Sort", "SortOrder>=0")); e.HasKey(x => x.Id); e.Property(x => x.Id).HasColumnName("ProductImageId").ValueGeneratedNever();
            e.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade); e.HasOne<ImageAsset>().WithMany().HasForeignKey(x => x.ImageAssetId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.ProductId, x.SortOrder, x.Id }); e.HasIndex(x => new { x.ProductId, x.ImageAssetId }).IsUnique(); e.HasIndex(x => x.ProductId).IsUnique().HasFilter("IsPrimary=1");
        });
        model.Entity<InventoryItemImage>(e =>
        {
            e.ToTable("InventoryItemImages", t => t.HasCheckConstraint("CK_ItemImage_Sort", "SortOrder>=0")); e.HasKey(x => x.Id); e.Property(x => x.Id).HasColumnName("InventoryItemImageId").ValueGeneratedNever();
            e.HasOne<InventoryItem>().WithMany().HasForeignKey(x => x.InventoryItemId).OnDelete(DeleteBehavior.Cascade); e.HasOne<ImageAsset>().WithMany().HasForeignKey(x => x.ImageAssetId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.InventoryItemId, x.SortOrder, x.Id }); e.HasIndex(x => new { x.InventoryItemId, x.ImageAssetId }).IsUnique();
        });
        model.Entity<TemporaryUpload>(e =>
        {
            e.ToTable("TemporaryUploads", t => { Hash(t, "SourceSha256"); Hash(t, "PolicyHash"); t.HasCheckConstraint("CK_Upload_Json", "json_valid(PolicyJson)"); t.HasCheckConstraint("CK_Upload_Values", "ByteLength>0 AND State IN ('pending','processing','ready','failed','consumed','cancelled') AND (State NOT IN ('pending','processing') OR StagingPath IS NOT NULL) AND (State!='ready' OR ImageAssetId IS NOT NULL) AND (State!='consumed' OR ConsumedAt IS NOT NULL)"); });
            e.HasKey(x => x.Id); e.Property(x => x.Id).HasColumnName("TemporaryUploadId").ValueGeneratedNever(); e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict); e.HasOne<ImageAsset>().WithMany().HasForeignKey(x => x.ImageAssetId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.UserId, x.ClientUploadKey }).IsUnique(); e.HasIndex(x => new { x.State, x.ExpiresAt }); e.HasIndex(x => x.ReplayUntil);
        });
        model.Entity<SystemAlert>(e =>
        {
            e.ToTable("SystemAlerts", t => t.HasCheckConstraint("CK_Alert_Json", "json_valid(SafeDetailsJson)")); e.HasKey(x => x.Id); e.Property(x => x.Id).HasColumnName("SystemAlertId").ValueGeneratedNever(); e.HasIndex(x => x.AlertKey).IsUnique(); e.HasIndex(x => x.ResolvedAt);
        });
    }
}
