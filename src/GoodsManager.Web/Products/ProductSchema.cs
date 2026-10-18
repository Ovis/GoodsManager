using GoodsManager.Web.Masters;
using Microsoft.EntityFrameworkCore;

namespace GoodsManager.Web.Products;

public sealed class Product
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string NormalizedName { get; set; } = "";
    public Guid CategoryId { get; set; }
    public string? Version { get; set; }
    public Guid? ManufacturerId { get; set; }
    public Guid? SellerId { get; set; }
    public string? Memo { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Revision { get; set; } = 1;
}
public sealed class ProductIdentifier
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public string Scheme { get; set; } = "";
    public string RawValue { get; set; } = "";
    public string NormalizedValue { get; set; } = "";
    public string ComparisonValue { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
public sealed class ProductLink
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public string Url { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
public sealed class ProductWork { public Guid ProductId { get; set; } public Guid WorkId { get; set; } }
public sealed class ProductCharacter { public Guid ProductId { get; set; } public Guid CharacterId { get; set; } }
public sealed class ProductTag { public Guid ProductId { get; set; } public Guid TagId { get; set; } }

public abstract class Ownership
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public string? Condition { get; set; }
    public string? StorageLocation { get; set; }
    public string? PurchasePlace { get; set; }
    public string? StorageLocationNormalized { get; set; }
    public string? PurchasePlaceNormalized { get; set; }
    public string? Memo { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
public sealed class InventoryLot : Ownership { public int Quantity { get; set; } }
public sealed class InventoryItem : Ownership;

public static class ProductSchema
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<ProductChange>(e =>
        {
            e.ToTable("ProductChanges", t => { t.HasCheckConstraint("CK_ProductChange_Kind", "Kind IN ('created','updated','trashed','restored','purged')"); t.HasCheckConstraint("CK_ProductChange_Revision", "Revision>=1"); });
            e.HasKey(x => x.Id); e.Property(x => x.Id).ValueGeneratedNever(); e.HasIndex(x => new { x.ProductId, x.OccurredAt });
        });
        model.Entity<Product>(e =>
        {
            e.ToTable("Products", t =>
            {
                t.HasCheckConstraint("CK_Product_Name", $"length(Name) BETWEEN 1 AND 200 AND length(trim(Name,{MasterSchema.Whitespace}))>0");
                t.HasCheckConstraint("CK_Product_Version", "Version IS NULL OR length(Version)<=200");
                t.HasCheckConstraint("CK_Product_Memo", "Memo IS NULL OR length(Memo)<=4000");
                t.HasCheckConstraint("CK_Product_Revision", "Revision>=1");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("ProductId").ValueGeneratedNever();
            e.Property(x => x.Revision).IsConcurrencyToken();
            e.HasOne<Category>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Organization>().WithMany().HasForeignKey(x => x.ManufacturerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Organization>().WithMany().HasForeignKey(x => x.SellerId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.DeletedAt, x.NormalizedName, x.Id });
        });
        model.Entity<ProductIdentifier>(e =>
        {
            e.ToTable("ProductIdentifiers", t =>
            {
                t.HasCheckConstraint("CK_Identifier_Raw", "length(RawValue) BETWEEN 1 AND 200");
                t.HasCheckConstraint("CK_Identifier_Normalized", "(Scheme='ean8' AND length(NormalizedValue)=8 AND NormalizedValue NOT GLOB '*[^0-9]*') OR (Scheme='upca' AND length(NormalizedValue)=12 AND NormalizedValue NOT GLOB '*[^0-9]*') OR (Scheme IN ('ean13','isbn13') AND length(NormalizedValue)=13 AND NormalizedValue NOT GLOB '*[^0-9]*') OR (Scheme='isbn10' AND length(NormalizedValue)=10 AND substr(NormalizedValue,1,9) NOT GLOB '*[^0-9]*' AND substr(NormalizedValue,10,1) GLOB '[0-9X]')");
                t.HasCheckConstraint("CK_Identifier_Comparison", "length(ComparisonValue)=14 AND ComparisonValue NOT GLOB '*[^0-9]*'");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("ProductIdentifierId").ValueGeneratedNever();
            e.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.ProductId, x.Scheme, x.NormalizedValue }).IsUnique();
            e.HasIndex(x => new { x.ComparisonValue, x.ProductId });
        });
        model.Entity<ProductLink>(e =>
        {
            e.ToTable("ProductLinks", t => t.HasCheckConstraint("CK_ProductLink_Url", "length(Url) BETWEEN 1 AND 2048 AND (lower(Url) GLOB 'https://?*' OR lower(Url) GLOB 'http://?*')"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("ProductLinkId").ValueGeneratedNever();
            e.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.ProductId, x.Id });
        });
        model.Entity<ProductWork>(e =>
        {
            e.ToTable("ProductWorks"); e.HasKey(x => new { x.ProductId, x.WorkId });
            e.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Work>().WithMany().HasForeignKey(x => x.WorkId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.WorkId, x.ProductId });
        });
        model.Entity<ProductCharacter>(e =>
        {
            e.ToTable("ProductCharacters"); e.HasKey(x => new { x.ProductId, x.CharacterId });
            e.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Character>().WithMany().HasForeignKey(x => x.CharacterId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.CharacterId, x.ProductId });
        });
        model.Entity<ProductTag>(e =>
        {
            e.ToTable("ProductTags"); e.HasKey(x => new { x.ProductId, x.TagId });
            e.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Tag>().WithMany().HasForeignKey(x => x.TagId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.TagId, x.ProductId });
        });
        Holding<InventoryLot>(model, "InventoryLots", "InventoryLotId");
        Holding<InventoryItem>(model, "InventoryItems", "InventoryItemId");
        model.Entity<InventoryLot>().ToTable("InventoryLots", t => t.HasCheckConstraint("CK_Lot_Quantity", "Quantity BETWEEN 1 AND 2147483647"));
    }

    private static void Holding<T>(ModelBuilder model, string table, string id) where T : Ownership
    {
        model.Entity<T>(e =>
        {
            // 共通C#基底クラスを単一のEF継承テーブルにはしない。
            e.HasBaseType((Type?)null);
            e.ToTable(table, t =>
            {
                t.HasCheckConstraint($"CK_{table}_Condition", "Condition IS NULL OR Condition IN ('unopened','opened','inUse')");
                t.HasCheckConstraint($"CK_{table}_Location", "StorageLocation IS NULL OR length(StorageLocation)<=200");
                t.HasCheckConstraint($"CK_{table}_Purchase", "PurchasePlace IS NULL OR length(PurchasePlace)<=200");
                t.HasCheckConstraint($"CK_{table}_Memo", "Memo IS NULL OR length(Memo)<=4000");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName(id).ValueGeneratedNever();
            e.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.ProductId, x.Id });
        });
    }
}
