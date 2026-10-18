using GoodsManager.Web.Masters;
using GoodsManager.Web.Storage;
using Microsoft.EntityFrameworkCore;
namespace GoodsManager.Web.Products;

public sealed partial class ProductOperations
{
    /// <summary>編集前の重複判定入力と属性を同じトランザクションから取得</summary>
    public async Task<ProductDraft> DraftAsync(Product product, CancellationToken token) => new(product.Name, product.CategoryId, product.Version, product.ManufacturerId, product.SellerId, product.Memo,
        await db.Set<ProductWork>().Where(x => x.ProductId == product.Id).Select(x => x.WorkId).ToArrayAsync(token),
        await db.Set<ProductCharacter>().Where(x => x.ProductId == product.Id).Select(x => x.CharacterId).ToArrayAsync(token),
        await db.Set<ProductTag>().Where(x => x.ProductId == product.Id).Select(x => x.TagId).ToArrayAsync(token),
        await db.Set<ProductIdentifier>().Where(x => x.ProductId == product.Id).Select(x => new IdentifierInput(x.Scheme, x.RawValue)).ToArrayAsync(token),
        await db.Set<ProductLink>().Where(x => x.ProductId == product.Id).Select(x => x.Url).ToArrayAsync(token));

    /// <summary>商品属性だけを全体置換し、変化があれば親の更新番号を一度進める</summary>
    public async Task UpdateAsync(Product product, ProductDraft draft, DateTimeOffset now, CancellationToken token)
    {
        RequireTransaction(); var codes = await ValidateDraftAsync(draft, token);
        var name = draft.Name.Trim(); var version = EmptyToNull(draft.Version?.Trim()); var memo = EmptyToNull(draft.Memo);
        var changed = product.Name != name || product.CategoryId != draft.CategoryId || product.Version != version || product.Memo != memo || product.ManufacturerId != draft.ManufacturerId || product.SellerId != draft.SellerId;
        product.Name = name; product.NormalizedName = MasterText.Normalize(name); product.CategoryId = draft.CategoryId; product.Version = version; product.Memo = memo; product.ManufacturerId = draft.ManufacturerId; product.SellerId = draft.SellerId;
        bool Relations<T>(T[] old, Guid[] desired, Func<T, Guid> key, Func<Guid, T> create) where T : class
        {
            var removed = old.Where(x => !desired.Contains(key(x))).ToArray(); db.RemoveRange(removed);
            var added = desired.Except(old.Select(key)).Select(create).ToArray(); db.AddRange(added); return removed.Length + added.Length > 0;
        }
        changed |= Relations(await db.Set<ProductWork>().Where(x => x.ProductId == product.Id).ToArrayAsync(token), draft.WorkIds, x => x.WorkId, id => new ProductWork { ProductId = product.Id, WorkId = id });
        changed |= Relations(await db.Set<ProductCharacter>().Where(x => x.ProductId == product.Id).ToArrayAsync(token), draft.CharacterIds, x => x.CharacterId, id => new ProductCharacter { ProductId = product.Id, CharacterId = id });
        changed |= Relations(await db.Set<ProductTag>().Where(x => x.ProductId == product.Id).ToArrayAsync(token), draft.TagIds, x => x.TagId, id => new ProductTag { ProductId = product.Id, TagId = id });
        var identifiers = await db.Set<ProductIdentifier>().Where(x => x.ProductId == product.Id).ToArrayAsync(token);
        foreach (var old in identifiers.Where(x => !codes.Any(c => c.Scheme == x.Scheme && c.NormalizedValue == x.NormalizedValue))) { db.Remove(old); changed = true; }
        foreach (var code in codes)
        {
            var old = identifiers.SingleOrDefault(x => x.Scheme == code.Scheme && x.NormalizedValue == code.NormalizedValue);
            if (old is null) { db.Add(new ProductIdentifier { Id = Guid.NewGuid(), ProductId = product.Id, Scheme = code.Scheme, RawValue = code.RawValue, NormalizedValue = code.NormalizedValue, ComparisonValue = code.ComparisonValue, CreatedAt = now, UpdatedAt = now }); changed = true; }
            else if (old.RawValue != code.RawValue || old.ComparisonValue != code.ComparisonValue) { old.RawValue = code.RawValue; old.ComparisonValue = code.ComparisonValue; old.UpdatedAt = now; changed = true; }
        }
        var links = (await db.Set<ProductLink>().Where(x => x.ProductId == product.Id).ToArrayAsync(token)).ToList();
        foreach (var url in draft.Links) { var old = links.FirstOrDefault(x => x.Url == url); if (old is not null) links.Remove(old); else { db.Add(new ProductLink { Id = Guid.NewGuid(), ProductId = product.Id, Url = url, CreatedAt = now, UpdatedAt = now }); changed = true; } }
        if (links.Count > 0) { db.RemoveRange(links); changed = true; }
        if (changed) await TouchAsync(product, now, token);
    }
}
