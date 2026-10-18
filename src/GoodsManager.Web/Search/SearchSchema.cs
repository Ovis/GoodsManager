using GoodsManager.Web.Products;
using Microsoft.EntityFrameworkCore;

namespace GoodsManager.Web.Search;

/// <summary>業務の正本から再構築できる検索用テキスト。</summary>
public sealed class ProductSearchDocument
{
    public Guid ProductId { get; set; }
    public string NameNormalized { get; set; } = "";
    public string WorksNormalized { get; set; } = "";
    public string CharactersNormalized { get; set; } = "";
    public string OtherNormalized { get; set; } = "";
    public int NormalizationVersion { get; set; } = 1;
    public DateTimeOffset UpdatedAt { get; set; }
}
public static class SearchSchema
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<ProductSearchDocument>(e =>
        {
            e.ToTable("ProductSearchDocuments", t => t.HasCheckConstraint("CK_SearchDocument_Version", "NormalizationVersion>=1"));
            e.HasKey(x => x.ProductId);
            e.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<Product>().HasIndex(x => new { x.CreatedAt, x.Id }).IsDescending(true, false).HasFilter("DeletedAt IS NULL");
        model.Entity<Product>().HasIndex(x => new { x.UpdatedAt, x.Id }).IsDescending(true, false).HasFilter("DeletedAt IS NULL");
        model.Entity<Product>().HasIndex(x => new { x.NormalizedName, x.Id }).HasFilter("DeletedAt IS NULL");
    }
}
