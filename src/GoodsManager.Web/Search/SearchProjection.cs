using GoodsManager.Web.Masters;
using GoodsManager.Web.Products;
using GoodsManager.Web.Storage;
using Microsoft.EntityFrameworkCore;

namespace GoodsManager.Web.Search;

/// <summary>検索投影の更新は正本・変更番号・成功記録と同じトランザクションへ参加する。</summary>
public sealed class SearchProjection(AppDbContext db)
{
    public async Task RebuildAllAsync(CancellationToken token = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        await RebuildAsync(await db.Set<Product>().Select(x => x.Id).ToArrayAsync(token), token);
        await transaction.CommitAsync(token);
    }

    public async Task<Guid[]> AffectedAsync(string kind, Guid? id, CancellationToken token)
    {
        if (!id.HasValue) return [];
        var value = id.Value;
        var query = db.Set<Product>().AsNoTracking();
        if (kind == "categories")
        {
            var categories = await db.Set<Category>().AsNoTracking().Select(x => new { x.Id, x.ParentCategoryId }).ToArrayAsync(token);
            var descendants = new HashSet<Guid> { value };
            var changed = true;
            while (changed)
            {
                changed = false;
                foreach (var category in categories)
                    if (category.ParentCategoryId.HasValue && descendants.Contains(category.ParentCategoryId.Value))
                        changed |= descendants.Add(category.Id);
            }
            return await query.Where(x => descendants.Contains(x.CategoryId)).Select(x => x.Id).ToArrayAsync(token);
        }
        query = kind switch
        {
            "works" => query.Where(p => db.Set<ProductWork>().Any(r => r.ProductId == p.Id && r.WorkId == value)),
            "series" => query.Where(p => db.Set<ProductWork>().Any(r => r.ProductId == p.Id && db.Set<WorkSeries>().Any(s => s.WorkId == r.WorkId && s.SeriesId == value))),
            "characters" => query.Where(p => db.Set<ProductCharacter>().Any(r => r.ProductId == p.Id && r.CharacterId == value)),
            "tags" => query.Where(p => db.Set<ProductTag>().Any(r => r.ProductId == p.Id && r.TagId == value)),
            "organizations" => query.Where(p => p.ManufacturerId == value || p.SellerId == value),
            _ => query.Where(p => false)
        };
        return await query.Select(p => p.Id).ToArrayAsync(token);
    }

    public async Task RebuildAsync(Guid[] ids, CancellationToken token)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("検索投影の更新には業務トランザクションが必要");
        ids = ids.Distinct().ToArray();
        if (ids.Length == 0) return;
        var productRows = await db.Set<Product>().AsNoTracking().Where(x => ids.Contains(x.Id)).Select(x => new { Product = x, CollectionId = EF.Property<Guid>(x, "CollectionId") }).ToArrayAsync(token);
        var products = productRows.Select(x => x.Product).ToArray();
        var collections = productRows.ToDictionary(x => x.Product.Id, x => x.CollectionId);
        var documents = await db.Set<ProductSearchDocument>().Where(x => ids.Contains(x.ProductId)).ToDictionaryAsync(x => x.ProductId, token);
        var categories = await db.Set<Category>().AsNoTracking().ToDictionaryAsync(x => x.Id, token);
        var works = await (from r in db.Set<ProductWork>() join w in db.Set<Work>() on r.WorkId equals w.Id where ids.Contains(r.ProductId) select new { r.ProductId, r.WorkId, w.Name }).ToArrayAsync(token);
        var workIds = works.Select(x => x.WorkId).Distinct().ToArray();
        var series = await (from r in db.Set<WorkSeries>() join s in db.Set<Series>() on r.SeriesId equals s.Id where workIds.Contains(r.WorkId) select new { r.WorkId, s.Name }).ToArrayAsync(token);
        var characters = await (from r in db.Set<ProductCharacter>() join c in db.Set<Character>() on r.CharacterId equals c.Id where ids.Contains(r.ProductId) select new { r.ProductId, c.Name }).ToArrayAsync(token);
        var tags = await (from r in db.Set<ProductTag>() join t in db.Set<Tag>() on r.TagId equals t.Id where ids.Contains(r.ProductId) select new { r.ProductId, t.Name }).ToArrayAsync(token);
        var orgIds = products.SelectMany(x => new[] { x.ManufacturerId, x.SellerId }).Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToArray();
        var organizations = await db.Set<Organization>().AsNoTracking().Where(x => orgIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, token);
        var identifiers = await db.Set<ProductIdentifier>().AsNoTracking().Where(x => ids.Contains(x.ProductId)).ToArrayAsync(token);
        var lots = await db.Set<InventoryLot>().Where(x => ids.Contains(x.ProductId)).ToArrayAsync(token);
        var items = await db.Set<InventoryItem>().Where(x => ids.Contains(x.ProductId)).ToArrayAsync(token);
        var holdings = lots.Cast<Ownership>().Concat(items).ToArray();
        foreach (var holding in holdings)
        {
            holding.StorageLocationNormalized = holding.StorageLocation is null ? null : MasterText.Normalize(holding.StorageLocation);
            holding.PurchasePlaceNormalized = holding.PurchasePlace is null ? null : MasterText.Normalize(holding.PurchasePlace);
        }
        foreach (var product in products)
        {
            var names = works.Where(x => x.ProductId == product.Id).ToArray();
            var categoryNames = new List<string>();
            Guid? categoryId = product.CategoryId;
            var visited = new HashSet<Guid>();
            while (categoryId.HasValue)
            {
                if (!visited.Add(categoryId.Value)) throw new InvalidDataException("カテゴリの保存値が循環している");
                var category = categories[categoryId.Value];
                categoryNames.Add(category.Name);
                categoryId = category.ParentCategoryId;
            }
            var own = holdings.Where(x => x.ProductId == product.Id).ToArray();
            var other = categoryNames.Cast<string?>().Concat(new[] { product.Version, product.Memo })
                .Concat(tags.Where(x => x.ProductId == product.Id).Select(x => x.Name))
                .Concat(series.Where(x => names.Any(w => w.WorkId == x.WorkId)).Select(x => x.Name))
                .Concat(new[] { product.ManufacturerId, product.SellerId }.Where(x => x.HasValue).Select(x => organizations[x!.Value].Name))
                .Concat(identifiers.Where(x => x.ProductId == product.Id).SelectMany(x => new[] { x.RawValue, x.NormalizedValue, x.ComparisonValue }))
                .Concat(own.SelectMany(x => new[] { x.StorageLocation, x.PurchasePlace, x.Memo }));
            var nameText = MasterText.Normalize(product.Name);
            var worksText = Text(names.Select(x => x.Name));
            var charactersText = Text(characters.Where(x => x.ProductId == product.Id).Select(x => x.Name));
            var otherText = Text(other);
            if (!documents.TryGetValue(product.Id, out var document))
            {
                document = new ProductSearchDocument { ProductId = product.Id };
                db.Add(document);
                db.Entry(document).Property("CollectionId").CurrentValue = collections[product.Id];
            }
            if (document.NameNormalized != nameText || document.WorksNormalized != worksText || document.CharactersNormalized != charactersText || document.OtherNormalized != otherText || document.NormalizationVersion != 1)
            {
                document.NameNormalized = nameText; document.WorksNormalized = worksText;
                document.CharactersNormalized = charactersText; document.OtherNormalized = otherText;
                document.NormalizationVersion = 1;
                // 派生データの再構築だけでは業務日時/Revision/DataRevisionを変えない。
                document.UpdatedAt = product.UpdatedAt;
            }
        }
        await db.SaveChangesAsync(token);
    }
    private static string Text(IEnumerable<string?> source) => string.Join('\n', source.Where(x => x is not null).Select(x => MasterText.Normalize(x!)).Where(x => x.Length > 0).Distinct().Order(StringComparer.Ordinal));
}
