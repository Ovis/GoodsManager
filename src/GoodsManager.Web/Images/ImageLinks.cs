using System.Text.Json;
using GoodsManager.Web.Masters;
using GoodsManager.Web.Products;
using GoodsManager.Web.Storage;
using Microsoft.EntityFrameworkCore;

namespace GoodsManager.Web.Images;

public interface IImageLink
{
    Guid Id { get; set; }
    Guid ImageAssetId { get; set; }
    int SortOrder { get; set; }
    DateTimeOffset CreatedAt { get; set; }
    DateTimeOffset UpdatedAt { get; set; }
}
public sealed record ImageRef(Guid LinkId, Guid AssetId, int SortOrder, bool IsPrimary, string ThumbnailUrl, string DisplayUrl, string? OriginalUrl);
public sealed record ImageEditEntry(Guid? LinkId, string? ClientRef, Guid? TemporaryUploadId, int SortOrder)
{
    public string Ref => LinkId?.ToString("D") ?? ClientRef!;
    public static ImageEditEntry[] Parse(JsonElement body)
    {
        var entries = body.ValueKind == JsonValueKind.Array ? body.EnumerateArray() : throw InputObject.Invalid("entries");
        var result = entries.Select(item =>
        {
            var x = new InputObject(item, "linkId", "clientRef", "temporaryUploadId", "replacementUploadId", "sortOrder");
            if (x.Has("linkId"))
            {
                if (x.Has("clientRef") || x.Has("temporaryUploadId")) throw InputObject.Invalid("entries");
                return new ImageEditEntry(x.Id("linkId"), null, x.OptionalId("replacementUploadId"), x.Integer("sortOrder", 0));
            }
            if (x.Has("replacementUploadId")) throw InputObject.Invalid("entries");
            var reference = x.Text("clientRef");
            if (string.IsNullOrWhiteSpace(reference) || Guid.TryParse(reference, out _)) throw InputObject.Invalid("clientRef");
            return new ImageEditEntry(null, reference, x.Id("temporaryUploadId"), x.Integer("sortOrder", 0));
        }).ToArray();
        if (result.Count(x => x.TemporaryUploadId.HasValue) > 100) throw InputObject.Invalid("entries");
        if (result.Select(x => x.Ref).Distinct(StringComparer.Ordinal).Count() != result.Length) throw InputObject.Invalid("entries");
        return result;
    }
}
public sealed class ImageLinks(AppDbContext db, TimeProvider clock)
{
    public int DeduplicatedCount { get; private set; }
    public static ImageRef Ref(IImageLink link, bool primary, bool original, Guid? collectionId = null) => new(link.Id, link.ImageAssetId, link.SortOrder, primary,
        $"/media/assets/{link.ImageAssetId:D}/thumbnail?collectionId={collectionId:D}", $"/media/assets/{link.ImageAssetId:D}/display?collectionId={collectionId:D}", original ? $"/media/assets/{link.ImageAssetId:D}/original?collectionId={collectionId:D}" : null);
    public static async Task<ImageRef[]> ProductRefsAsync(AppDbContext db, Guid productId, CancellationToken token)
    {
        var data = await (from link in db.Set<ProductImage>().AsNoTracking() join asset in db.Set<ImageAsset>() on link.ImageAssetId equals asset.Id where link.ProductId == productId orderby link.SortOrder, link.Id select new { Link = link, asset.KeepOriginal }).ToArrayAsync(token);
        return data.Select(x => Ref(x.Link, x.Link.IsPrimary, x.KeepOriginal, db.CollectionId)).ToArray();
    }
    /// <summary>既存の関連と代表を保ち、新規アップロードだけを末尾へ追加</summary>
    public async Task AppendProductAsync(Guid productId, Guid userId, Guid[] uploads, CancellationToken token)
    {
        if (uploads.Length == 0) return;
        var existing = await ProductRefsAsync(db, productId, token);
        var entries = existing.Select((x, i) => new ImageEditEntry(x.LinkId, null, null, i)).Concat(uploads.Select((id, i) => new ImageEditEntry(null, "append-" + i, id, checked(existing.Length + i)))).ToArray();
        await ReplaceProductAsync(productId, userId, entries, null, token);
    }
    public async Task<bool> ReplaceProductAsync(Guid productId, Guid userId, ImageEditEntry[] entries, string? primaryRef, CancellationToken token)
    {
        var old = await db.Set<ProductImage>().Where(x => x.ProductId == productId).ToListAsync(token);
        var links = Current(old, db.Set<ProductImage>().Local.Where(x => x.ProductId == productId)).Cast<IImageLink>().ToList();
        return await ReplaceAsync(links, entries, userId, primaryRef, () => new ProductImage { ProductId = productId }, token);
    }
    public async Task<bool> ReplaceItemAsync(Guid itemId, Guid userId, ImageEditEntry[] entries, CancellationToken token)
    {
        var old = await db.Set<InventoryItemImage>().Where(x => x.InventoryItemId == itemId).ToListAsync(token);
        var links = Current(old, db.Set<InventoryItemImage>().Local.Where(x => x.InventoryItemId == itemId)).Cast<IImageLink>().ToList();
        return await ReplaceAsync(links, entries, userId, null, () => new InventoryItemImage { InventoryItemId = itemId }, token);
    }
    private IEnumerable<T> Current<T>(IEnumerable<T> queried, IEnumerable<T> local) where T : class, IImageLink => queried.Concat(local).DistinctBy(x => x.Id).Where(x => db.Entry(x).State != EntityState.Deleted);
    private async Task<bool> ReplaceAsync(List<IImageLink> old, ImageEditEntry[] entries, Guid userId, string? primaryRef, Func<IImageLink> create, CancellationToken token)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("画像関連の更新には業務トランザクションが必要");
        if (primaryRef is not null && !entries.Any(x => x.Ref == primaryRef)) throw InputObject.Invalid("primaryRef");
        var now = clock.GetUtcNow(); var plans = new List<(ImageEditEntry Entry, Guid Asset, IImageLink? Existing)>(); var uploads = new List<TemporaryUpload>();
        foreach (var entry in entries)
        {
            var link = entry.LinkId.HasValue ? old.SingleOrDefault(x => x.Id == entry.LinkId) ?? throw new MasterProblem(400, "IMAGE_LINK_NOT_OWNED") : null;
            var asset = link?.ImageAssetId ?? Guid.Empty;
            if (entry.TemporaryUploadId.HasValue)
            {
                var upload = await db.Set<TemporaryUpload>().SingleOrDefaultAsync(x => x.Id == entry.TemporaryUploadId && x.UserId == userId, token) ?? throw new MasterProblem(400, "UPLOAD_NOT_OWNED");
                if (upload.State != "ready" || upload.ExpiresAt <= now || !upload.ImageAssetId.HasValue || uploads.Any(x => x.Id == upload.Id)) throw new MasterProblem(409, "UPLOAD_NOT_READY");
                asset = upload.ImageAssetId.Value; uploads.Add(upload);
            }
            if (asset == Guid.Empty) throw InputObject.Invalid("entries");
            plans.Add((entry, asset, link));
        }
        DeduplicatedCount += plans.Count - plans.Select(x => x.Asset).Distinct().Count();
        var bindings = plans.Where(x => x.Existing is not null).ToDictionary(x => x.Existing!.Id, x => x.Asset);
        var final = new List<(IImageLink Link, Guid Asset, int Sort)>(); var aliases = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var group in plans.GroupBy(x => x.Asset))
        {
            // 同じアセットの既存リンクを優先し、IDと重複しない関連を維持する。
            var preferred = group.FirstOrDefault(x => x.Existing?.ImageAssetId == group.Key);
            if (preferred.Entry is null) preferred = group.First();
            var existing = old.FirstOrDefault(x => x.ImageAssetId == group.Key && (!bindings.TryGetValue(x.Id, out var target) || target == group.Key));
            var link = existing ?? preferred.Existing ?? create();
            if (link.Id == Guid.Empty) { link.Id = Guid.NewGuid(); link.CreatedAt = now; }
            final.Add((link, group.Key, preferred.Entry.SortOrder));
            foreach (var plan in group) aliases.Add(plan.Entry.Ref, link.Id);
        }
        var primaryId = primaryRef is not null ? aliases[primaryRef] : old.OfType<ProductImage>().Where(x => x.IsPrimary).Select(x => (Guid?)x.Id).FirstOrDefault();
        if (!final.Any(x => x.Link.Id == primaryId)) primaryId = final.OrderBy(x => x.Sort).ThenBy(x => x.Link.Id).Select(x => (Guid?)x.Link.Id).FirstOrDefault();
        var changed = final.Count != old.Count || final.Any(x => !old.Contains(x.Link) || x.Link.ImageAssetId != x.Asset || x.Link.SortOrder != x.Sort || x.Link is ProductImage p && p.IsPrimary != (p.Id == primaryId));
        if (changed)
        {
            // 部分一意索引に触れない順序で代表を切り替える。途中失敗は外側のトランザクションで取消。
            foreach (var link in old.OfType<ProductImage>()) link.IsPrimary = false;
            await db.SaveChangesAsync(token);
            db.RemoveRange(old.Where(x => !final.Any(f => f.Link.Id == x.Id) || final.Any(f => f.Link.Id == x.Id && f.Asset != x.ImageAssetId)));
            // アセット交換のUK衝突を避けるため、最終リストから外れた行の削除を先に確定（未コミット）。
            await db.SaveChangesAsync(token);
            foreach (var entry in final)
            {
                entry.Link.ImageAssetId = entry.Asset; entry.Link.SortOrder = entry.Sort; entry.Link.UpdatedAt = now;
                if (entry.Link is ProductImage p) p.IsPrimary = p.Id == primaryId;
                if (!old.Contains(entry.Link) || db.Entry(entry.Link).State == EntityState.Detached) db.Add(entry.Link);
            }
        }
        foreach (var upload in uploads) { upload.State = "consumed"; upload.ConsumedAt = now; upload.ImageAssetId = null; upload.UpdatedAt = now; }
        return changed;
    }
    public async Task MoveItemImagesAsync(Guid itemId, Guid productId, bool move, CancellationToken token)
    {
        var old = await db.Set<InventoryItemImage>().Where(x => x.InventoryItemId == itemId).ToListAsync(token);
        var itemImages = Current(old, db.Set<InventoryItemImage>().Local.Where(x => x.InventoryItemId == itemId)).ToArray();
        var productOld = await db.Set<ProductImage>().Where(x => x.ProductId == productId).ToListAsync(token);
        var products = Current(productOld, db.Set<ProductImage>().Local.Where(x => x.ProductId == productId)).ToList();
        if (move) foreach (var image in itemImages.OrderBy(x => x.SortOrder).ThenBy(x => x.Id))
            if (!products.Any(x => x.ImageAssetId == image.ImageAssetId))
            {
                var now = clock.GetUtcNow(); var link = new ProductImage { Id = Guid.NewGuid(), ProductId = productId, ImageAssetId = image.ImageAssetId, SortOrder = products.Count == 0 ? 0 : checked(products.Max(x => x.SortOrder) + 1), IsPrimary = products.Count == 0, CreatedAt = now, UpdatedAt = now }; products.Add(link); db.Add(link);
            }
        db.RemoveRange(itemImages);
    }
}
