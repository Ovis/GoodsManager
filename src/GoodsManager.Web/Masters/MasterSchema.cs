using GoodsManager.Web.Auth;
using Microsoft.EntityFrameworkCore;

namespace GoodsManager.Web.Masters;

public interface INamedMaster
{
    Guid Id { get; set; }
    string Name { get; set; }
    string NormalizedName { get; set; }
    DateTimeOffset CreatedAt { get; set; }
    DateTimeOffset UpdatedAt { get; set; }
    long Revision { get; set; }
}

public sealed class Work : INamedMaster
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string NormalizedName { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Revision { get; set; } = 1;
}
public sealed class Series : INamedMaster
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string NormalizedName { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Revision { get; set; } = 1;
}
public sealed class Character : INamedMaster
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string NormalizedName { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Revision { get; set; } = 1;
}
public sealed class Tag : INamedMaster
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string NormalizedName { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Revision { get; set; } = 1;
}
public sealed class Organization : INamedMaster
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string NormalizedName { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Revision { get; set; } = 1;
    public string? WebsiteUrl { get; set; }
    public string? Memo { get; set; }
}
public sealed class Category : INamedMaster
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string NormalizedName { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Revision { get; set; } = 1;
    public Guid? ParentCategoryId { get; set; }
    public int SortOrder { get; set; }
}
public sealed class WorkSeries
{
    public Guid WorkId { get; set; }
    public Guid SeriesId { get; set; }
}
public sealed class CharacterWork
{
    public Guid CharacterId { get; set; }
    public Guid WorkId { get; set; }
}
public sealed class IdempotencyRecord
{
    public Guid UserId { get; set; }
    public string OperationScope { get; set; } = "";
    public Guid Key { get; set; }
    public string RequestHash { get; set; } = "";
    public int ResponseStatus { get; set; }
    public string ResponseJson { get; set; } = "";
    public string? Location { get; set; }
    public string ResponseEtag { get; set; } = "";
    public DateTimeOffset CompletedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

/// <summary>マスターごとの通常テーブルと、関連・成功再送記録の永続制約</summary>
public static class MasterSchema
{
    // .NETのUnicode空白集合。表示名は保持し、空白だけの名前をDBでも拒否する。
    private const string Whitespace = "char(9,10,11,12,13,32,133,160,5760,8192,8193,8194,8195,8196,8197,8198,8199,8200,8201,8202,8232,8233,8239,8287,12288)";

    public static void Configure(ModelBuilder model)
    {
        Named<Work>(model, "Works", "WorkId");
        Named<Series>(model, "Series", "SeriesId");
        Named<Character>(model, "Characters", "CharacterId");
        Named<Tag>(model, "Tags", "TagId");
        Named<Organization>(model, "Organizations", "OrganizationId");
        Named<Category>(model, "Categories", "CategoryId");
        model.Entity<Organization>().ToTable("Organizations", t =>
        {
            t.HasCheckConstraint("CK_Organization_Memo", "Memo IS NULL OR length(Memo)<=4000");
            t.HasCheckConstraint("CK_Organization_Url", "WebsiteUrl IS NULL OR (length(WebsiteUrl) BETWEEN 1 AND 2048 AND (lower(WebsiteUrl) GLOB 'https://?*' OR lower(WebsiteUrl) GLOB 'http://?*'))");
        });
        model.Entity<Category>(e =>
        {
            e.HasOne<Category>().WithMany().HasForeignKey(x => x.ParentCategoryId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.ParentCategoryId, x.SortOrder, x.Id });
            e.ToTable("Categories", t =>
            {
                t.HasCheckConstraint("CK_Category_Parent", "ParentCategoryId IS NULL OR ParentCategoryId<>CategoryId");
                t.HasCheckConstraint("CK_Category_Order", "SortOrder>=0");
            });
        });
        model.Entity<WorkSeries>(e =>
        {
            e.ToTable("WorkSeries");
            e.HasKey(x => new { x.WorkId, x.SeriesId });
            e.HasIndex(x => new { x.SeriesId, x.WorkId });
            e.HasOne<Work>().WithMany().HasForeignKey(x => x.WorkId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Series>().WithMany().HasForeignKey(x => x.SeriesId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<CharacterWork>(e =>
        {
            e.ToTable("CharacterWorks");
            e.HasKey(x => new { x.CharacterId, x.WorkId });
            e.HasIndex(x => new { x.WorkId, x.CharacterId });
            e.HasOne<Character>().WithMany().HasForeignKey(x => x.CharacterId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Work>().WithMany().HasForeignKey(x => x.WorkId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<IdempotencyRecord>(e =>
        {
            e.ToTable("IdempotencyRecords", t =>
            {
                t.HasCheckConstraint("CK_Idempotency_Hash", "length(RequestHash)=64 AND RequestHash NOT GLOB '*[^0-9a-f]*'");
                t.HasCheckConstraint("CK_Idempotency_Response", "ResponseStatus IN (200,201) AND json_valid(ResponseJson)=1 AND json_type(ResponseJson)='object'");
                t.HasCheckConstraint("CK_Idempotency_Expiry", "ExpiresAt>CompletedAt");
            });
            e.HasKey(x => new { x.UserId, x.OperationScope, x.Key });
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.ExpiresAt);
        });
    }

    private static void Named<T>(ModelBuilder model, string table, string id) where T : class, INamedMaster
    {
        model.Entity<T>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName(id).ValueGeneratedNever();
            e.Property(x => x.Name).IsRequired();
            e.Property(x => x.NormalizedName).IsRequired();
            e.Property(x => x.Revision).IsConcurrencyToken();
            e.HasIndex(x => new { x.NormalizedName, x.Id });
            e.ToTable(table, t =>
            {
                t.HasCheckConstraint($"CK_{table}_Name", $"length(Name) BETWEEN 1 AND 200 AND length(trim(Name,{Whitespace}))>0");
                t.HasCheckConstraint($"CK_{table}_Revision", "Revision>=1");
            });
        });
    }
}
