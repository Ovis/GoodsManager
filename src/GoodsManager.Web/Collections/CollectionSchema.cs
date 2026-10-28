using GoodsManager.Web.Auth;
using GoodsManager.Web.Images;
using GoodsManager.Web.Masters;
using GoodsManager.Web.Products;
using GoodsManager.Web.Search;
using Microsoft.EntityFrameworkCore;

namespace GoodsManager.Web.Collections;

public sealed class CollectionSpace
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public long Revision { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
public sealed class CollectionMember
{
    public Guid CollectionId { get; set; }
    public Guid UserId { get; set; }
    public string Role { get; set; } = "manager";
}

/// <summary>所属を業務テーブルと関連テーブルの両方に持たせ、DBでもコレクション間の参照を拒否する。</summary>
public static class CollectionSchema
{
    public static readonly Guid DefaultId = Guid.Parse("c011ec70-0000-4000-8000-000000000001");
    public static readonly Type[] ScopedTypes = [typeof(Work), typeof(Series), typeof(Character), typeof(Tag), typeof(Organization), typeof(Category), typeof(WorkSeries), typeof(CharacterWork), typeof(Product), typeof(ProductIdentifier), typeof(ProductLink), typeof(ProductWork), typeof(ProductCharacter), typeof(ProductTag), typeof(InventoryLot), typeof(InventoryItem), typeof(ProductChange), typeof(ProductSearchDocument), typeof(ImageSettings), typeof(ImageAsset), typeof(ProductImage), typeof(InventoryItemImage), typeof(TemporaryUpload), typeof(IdempotencyRecord)];

    public static void Configure(ModelBuilder model)
    {
        model.Entity<CollectionSpace>(e =>
        {
            e.ToTable("Collections", t => { t.HasCheckConstraint("CK_Collection_Name", $"length(Name) BETWEEN 1 AND 200 AND length(trim(Name,{MasterSchema.Whitespace}))>0"); t.HasCheckConstraint("CK_Collection_Revision", "Revision>=1"); });
            e.HasKey(x => x.Id); e.Property(x => x.Id).ValueGeneratedNever(); e.Property(x => x.Revision).IsConcurrencyToken();
            e.HasData(new CollectionSpace { Id = DefaultId, Name = "マイコレクション", Revision = 1, CreatedAt = DateTimeOffset.UnixEpoch, UpdatedAt = DateTimeOffset.UnixEpoch });
        });
        model.Entity<CollectionMember>(e =>
        {
            e.ToTable("CollectionMembers", t => t.HasCheckConstraint("CK_CollectionMember_Role", "Role IN ('manager','editor')"));
            e.HasKey(x => new { x.CollectionId, x.UserId });
            e.HasOne<CollectionSpace>().WithMany().HasForeignKey(x => x.CollectionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.UserId, x.CollectionId });
        });
        foreach (var type in ScopedTypes)
        {
            var e = model.Entity(type);
            e.Property<Guid>("CollectionId").HasDefaultValue(DefaultId).ValueGeneratedNever();
            e.HasIndex("CollectionId");
        }
        model.Entity<ImageSettings>().HasKey("CollectionId");
        model.Entity<IdempotencyRecord>().HasKey("CollectionId", "UserId", "OperationScope", "Key");
        foreach (var (type, columns) in new[] { (typeof(ImageAsset), new[] { "SourceSha256", "PolicyHash" }), (typeof(TemporaryUpload), new[] { "UserId", "ClientUploadKey" }) })
        {
            var entity = model.Entity(type); var index = entity.Metadata.FindIndex(columns.Select(x => entity.Metadata.FindProperty(x)!).ToArray());
            if (index is not null) entity.Metadata.RemoveIndex(index);
            entity.HasIndex(new[] { "CollectionId" }.Concat(columns).ToArray()).IsUnique();
        }
        foreach (var type in ScopedTypes)
        {
            var entity = model.Entity(type);
            foreach (var foreign in entity.Metadata.GetForeignKeys().Where(x => ScopedTypes.Contains(x.PrincipalEntityType.ClrType)).ToArray())
            {
                var principal = foreign.PrincipalEntityType.ClrType;
                var from = new[] { "CollectionId" }.Concat(foreign.Properties.Select(x => x.Name)).Distinct().ToArray();
                var to = new[] { "CollectionId" }.Concat(foreign.PrincipalKey.Properties.Select(x => x.Name)).Distinct().ToArray();
                var delete = foreign.DeleteBehavior;
                entity.Metadata.RemoveForeignKey(foreign);
                model.Entity(principal).HasAlternateKey(to);
                entity.HasOne(principal).WithMany().HasForeignKey(from).HasPrincipalKey(to).OnDelete(delete);
            }
            entity.HasOne(typeof(CollectionSpace)).WithMany().HasForeignKey("CollectionId").OnDelete(DeleteBehavior.Restrict);
        }
        SeedCategories(model);
    }

    private static void SeedCategories(ModelBuilder model)
    {
        string[][] names = [["アクリル・キーホルダー類", "アクリルスタンド", "アクリルキーホルダー", "ラバーキーホルダー", "メタルキーホルダー", "その他"], ["バッジ類", "缶バッジ", "ピンバッジ", "その他"], ["紙・印刷物", "クリアファイル", "ポスター", "タペストリー", "ポストカード", "ブロマイド", "色紙", "ステッカー・シール", "その他"], ["フィギュア・立体物", "フィギュア", "ぬいぐるみ", "その他"], ["書籍", "書籍", "雑誌", "漫画", "同人誌", "パンフレット", "その他"], ["音楽・映像", "CD", "DVD", "Blu-ray", "その他"], ["衣類・布製品", "Tシャツ", "その他衣類", "タオル", "バッグ", "その他"], ["食器・生活雑貨", "マグカップ・グラス", "食器", "文房具", "その他"], ["その他"]];
        for (var root = 0; root < names.Length; root++) for (var child = 0; child < names[root].Length; child++)
        {
            Guid Id(int n) => Guid.Parse($"b390a000-7024-4c01-a000-{n:000000000000}");
            model.Entity<Category>().HasData(new { Id = Id((root + 1) * 100 + child), CollectionId = DefaultId, Name = names[root][child], NormalizedName = MasterText.Normalize(names[root][child]), CreatedAt = DateTimeOffset.UnixEpoch, UpdatedAt = DateTimeOffset.UnixEpoch, Revision = 1L, ParentCategoryId = child == 0 ? (Guid?)null : Id((root + 1) * 100), SortOrder = child == 0 ? root : child - 1 });
        }
    }
}
