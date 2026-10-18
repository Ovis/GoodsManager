using System.Globalization;
using GoodsManager.Web.Masters;
using GoodsManager.Web.Images;
using GoodsManager.Web.Storage;
using Microsoft.EntityFrameworkCore;

namespace GoodsManager.Web.Products;

/// <summary>P08の通常登録処理からも利用する保存処理。トランザクションの確定と再送記録は呼出側が所有する。</summary>
public sealed partial class ProductOperations(AppDbContext db)
{
    public static string Etag(Product product) => $"\"product:{product.Id:D}:{product.Revision.ToString(CultureInfo.InvariantCulture)}\"";
    public async Task<Product> FindAsync(Guid id, CancellationToken token) =>
        await db.Set<Product>().SingleOrDefaultAsync(x => x.Id == id && x.DeletedAt == null, token) ?? throw new MasterProblem(404, "NOT_FOUND");

    public async Task<Product> CreateAsync(ProductDraft draft, int quantity, OwnershipPatch fields, DateTimeOffset now, CancellationToken token)
    {
        RequireTransaction();
        var codes = await ValidateDraftAsync(draft, token);
        if (quantity < 1) throw InputObject.Invalid("quantity");
        ValidateFields(fields);
        var name = draft.Name.Trim();
        var product = new Product
        {
            Id = Guid.NewGuid(),
            Name = name,
            NormalizedName = MasterText.Normalize(name),
            CategoryId = draft.CategoryId,
            Version = EmptyToNull(draft.Version?.Trim()),
            ManufacturerId = draft.ManufacturerId,
            SellerId = draft.SellerId,
            Memo = EmptyToNull(draft.Memo),
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Add(product);
        foreach (var id in draft.WorkIds) db.Add(new ProductWork { ProductId = product.Id, WorkId = id });
        foreach (var id in draft.CharacterIds) db.Add(new ProductCharacter { ProductId = product.Id, CharacterId = id });
        foreach (var id in draft.TagIds) db.Add(new ProductTag { ProductId = product.Id, TagId = id });
        foreach (var code in codes) db.Add(new ProductIdentifier { Id = Guid.NewGuid(), ProductId = product.Id, Scheme = code.Scheme, RawValue = code.RawValue, NormalizedValue = code.NormalizedValue, ComparisonValue = code.ComparisonValue, CreatedAt = now, UpdatedAt = now });
        foreach (var url in draft.Links) db.Add(new ProductLink { Id = Guid.NewGuid(), ProductId = product.Id, Url = url, CreatedAt = now, UpdatedAt = now });
        var lot = new InventoryLot { Id = Guid.NewGuid(), ProductId = product.Id, Quantity = quantity, CreatedAt = now, UpdatedAt = now };
        fields.Apply(lot);
        db.Add(lot);
        await ChangeRevisionAsync(now, token);
        return product;
    }

    /// <summary>作成・編集に共通する属性、参照、コード、URLの検証</summary>
    private async Task<List<IdentifierValue>> ValidateDraftAsync(ProductDraft draft, CancellationToken token)
    {
        if (!StorageValues.IsValidText(draft.Name, 1, 200) || string.IsNullOrWhiteSpace(draft.Name)) throw InputObject.Invalid("name");
        CheckText(draft.Version, 200, "version");
        CheckText(draft.Memo, 4000, "memo");
        if (draft.CategoryId == Guid.Empty || !await db.Set<Category>().AnyAsync(x => x.Id == draft.CategoryId, token)) throw new MasterProblem(400, "RELATED_NOT_FOUND");
        var organizations = new[] { draft.ManufacturerId, draft.SellerId }.Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToArray();
        if (organizations.Contains(Guid.Empty) || await db.Set<Organization>().CountAsync(x => organizations.Contains(x.Id), token) != organizations.Length) throw new MasterProblem(400, "RELATED_NOT_FOUND");
        await CheckIds<Work>(draft.WorkIds, token);
        await CheckIds<Character>(draft.CharacterIds, token);
        await CheckIds<Tag>(draft.TagIds, token);
        var codes = new List<IdentifierValue>();
        foreach (var input in draft.Identifiers)
        {
            if (!StorageValues.IsValidText(input.Value, 1, 200) || !IdentifierCodes.TryNormalize(input.Scheme, input.Value, out var code)) throw InputObject.Invalid("identifiers");
            codes.Add(code);
        }
        if (codes.DistinctBy(x => (x.Scheme, x.NormalizedValue)).Count() != codes.Count) throw InputObject.Invalid("identifiers");
        foreach (var url in draft.Links)
        {
            if (!StorageValues.IsValidText(url, 1, 2048) || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
                || uri.Scheme is not ("http" or "https") || string.IsNullOrEmpty(uri.Host)
                || url.Contains('\\') || url.Any(c => char.IsWhiteSpace(c) || char.IsControl(c))) throw InputObject.Invalid("links");
        }
        return codes;
    }

    private async Task CheckIds<T>(Guid[] ids, CancellationToken token) where T : class, INamedMaster
    {
        if (ids.Contains(Guid.Empty) || ids.Distinct().Count() != ids.Length) throw InputObject.Invalid("ids");
        if (await db.Set<T>().CountAsync(x => ids.Contains(x.Id), token) != ids.Length) throw new MasterProblem(400, "RELATED_NOT_FOUND");
    }
    internal void RequireTransaction()
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("商品変更は呼出側のトランザクション内で行う");
    }
    internal async Task TouchAsync(Product product, DateTimeOffset now, CancellationToken token)
    {
        product.Revision = checked(product.Revision + 1);
        product.UpdatedAt = now;
        await ChangeRevisionAsync(now, token);
    }
    private async Task ChangeRevisionAsync(DateTimeOffset now, CancellationToken token)
    {
        var state = await db.SystemStates.SingleAsync(token);
        state.DataRevision = checked(state.DataRevision + 1);
        state.UpdatedAt = now;
    }
    private static string? EmptyToNull(string? value) => value?.Length == 0 ? null : value;
    private static void CheckText(string? value, int maximum, string field)
    {
        if (value is not null && !StorageValues.IsValidText(value, 0, maximum)) throw InputObject.Invalid(field);
    }
    internal static void ValidateFields(OwnershipPatch fields)
    {
        if (fields.Condition.HasValue && fields.Condition.Value is not (null or "unopened" or "opened" or "inUse")) throw InputObject.Invalid("condition");
        if (fields.StorageLocation.HasValue) CheckText(fields.StorageLocation.Value, 200, "storageLocation");
        if (fields.PurchasePlace.HasValue) CheckText(fields.PurchasePlace.Value, 200, "purchasePlace");
        if (fields.Memo.HasValue) CheckText(fields.Memo.Value, 4000, "memo");
    }

    public async Task<object> DetailAsync(Product product, CancellationToken token)
    {
        var category = await db.Set<Category>().AsNoTracking().SingleAsync(x => x.Id == product.CategoryId, token);
        var organizations = await db.Set<Organization>().AsNoTracking().Where(x => x.Id == product.ManufacturerId || x.Id == product.SellerId).ToDictionaryAsync(x => x.Id, token);
        object? Org(Guid? id) => id.HasValue ? new { id = id.Value, organizations[id.Value].Name } : null;
        var works = await (from r in db.Set<ProductWork>() join m in db.Set<Work>() on r.WorkId equals m.Id where r.ProductId == product.Id orderby m.Id select new { m.Id, m.Name }).ToArrayAsync(token);
        var characters = await (from r in db.Set<ProductCharacter>() join m in db.Set<Character>() on r.CharacterId equals m.Id where r.ProductId == product.Id orderby m.Id select new { m.Id, m.Name }).ToArrayAsync(token);
        var tags = await (from r in db.Set<ProductTag>() join m in db.Set<Tag>() on r.TagId equals m.Id where r.ProductId == product.Id orderby m.Id select new { m.Id, m.Name }).ToArrayAsync(token);
        var identifiers = await db.Set<ProductIdentifier>().AsNoTracking().Where(x => x.ProductId == product.Id).OrderBy(x => x.Id).Select(x => new { x.Id, x.Scheme, x.RawValue, x.NormalizedValue, x.ComparisonValue }).ToArrayAsync(token);
        var links = await db.Set<ProductLink>().AsNoTracking().Where(x => x.ProductId == product.Id).OrderBy(x => x.Id).Select(x => new { x.Id, x.Url }).ToArrayAsync(token);
        var inventory = await InventoryAsync(product, new Dictionary<string, Guid>(), token);
        var images = await ImageLinks.ProductRefsAsync(db, product.Id, token);
        return new
        {
            product.Id,
            product.Name,
            category = new { category.Id, category.Name },
            product.Version,
            product.Memo,
            manufacturer = Org(product.ManufacturerId),
            seller = Org(product.SellerId),
            works,
            characters,
            tags,
            identifiers,
            links,
            images,
            primaryImage = images.FirstOrDefault(x => x.IsPrimary),
            inventory.TotalQuantity,
            inventory,
            product.CreatedAt,
            product.UpdatedAt,
            product.DeletedAt,
            revision = product.Revision.ToString(CultureInfo.InvariantCulture),
            etag = Etag(product)
        };
    }

    public async Task<InventoryResult> InventoryAsync(Product product, IReadOnlyDictionary<string, Guid> refs, CancellationToken token)
    {
        var lots = await db.Set<InventoryLot>().AsNoTracking().Where(x => x.ProductId == product.Id).OrderBy(x => x.Id).ToArrayAsync(token);
        var items = await db.Set<InventoryItem>().AsNoTracking().Where(x => x.ProductId == product.Id).OrderBy(x => x.Id).ToArrayAsync(token);
        var itemIds = items.Select(x => x.Id).ToArray();
        var itemImages = await (from link in db.Set<InventoryItemImage>().AsNoTracking() join asset in db.Set<ImageAsset>() on link.ImageAssetId equals asset.Id where itemIds.Contains(link.InventoryItemId) orderby link.SortOrder, link.Id select new { Link = link, asset.KeepOriginal }).ToArrayAsync(token);
        return new(product.Revision.ToString(CultureInfo.InvariantCulture), Etag(product), Total(lots, items), lots.Select(x => new LotView(x.Id, x.Quantity, x.Condition, x.StorageLocation, x.PurchasePlace, x.Memo, x.CreatedAt, x.UpdatedAt)).ToArray(),
            items.Select(x => new ItemView(x.Id, x.Condition, x.StorageLocation, x.PurchasePlace, x.Memo, x.CreatedAt, x.UpdatedAt, itemImages.Where(i => i.Link.InventoryItemId == x.Id).Select(i => ImageLinks.Ref(i.Link, false, i.KeepOriginal)).ToArray())).ToArray(), refs);
    }

    internal static long Total(IEnumerable<InventoryLot> lots, IEnumerable<InventoryItem> items)
    {
        // 関連マスターとのJOINを行わず、個体とロットを別々に数える。
        return QuantityTotals.Calculate(lots.Select(x => x.Quantity), items.LongCount());
    }
}
public sealed record LotView(Guid Id, int Quantity, string? Condition, string? StorageLocation, string? PurchasePlace, string? Memo, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record ItemView(Guid Id, string? Condition, string? StorageLocation, string? PurchasePlace, string? Memo, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, ImageRef[] Images);
public sealed record InventoryResult(string ProductRevision, string Etag, long TotalQuantity, LotView[] Lots, ItemView[] Items, IReadOnlyDictionary<string, Guid> CreatedRefs, int DeduplicatedCount = 0);
