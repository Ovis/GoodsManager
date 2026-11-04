using GoodsManager.Web.Images;
using GoodsManager.Web.Masters;
using GoodsManager.Web.Products;
using GoodsManager.Web.Search;
using GoodsManager.Web.Storage;
using Microsoft.EntityFrameworkCore;

namespace GoodsManager.Web.Collections;

/// <summary>削除確認に必要な件数と、確認後の変更を検出する識別値。</summary>
public sealed record CollectionDeleteImpact(Guid Id, string Name, string Etag, string Confirmation, bool CanDelete,
    int Products, int TrashedProducts, int Lots, int Items, int Images, int Masters);

/// <summary>コレクションの全所属データを外部キーの順に削除し、共有画像ファイルは既存GCへ任せる。</summary>
public sealed class CollectionDeletion(AppDbContext db)
{
    /// <summary>確認情報を同じトランザクションで読み、他の利用者も所属を失わないことを検査する。</summary>
    public async Task<CollectionDeleteImpact> ImpactAsync(CollectionSpace space, CancellationToken token)
    {
        var id = space.Id;
        var members = db.Set<CollectionMember>().Where(x => x.CollectionId == id);
        var protectedMember = await members.AnyAsync(member => !db.Set<CollectionMember>().Any(other => other.UserId == member.UserId && other.CollectionId != id), token);
        var revision = (await db.SystemStates.AsNoTracking().SingleAsync(token)).DataRevision;

        return new CollectionDeleteImpact(id, space.Name, CollectionWrites.Etag(space), $"{id:D}:{revision}", !protectedMember,
            await Rows<Product>(id).CountAsync(x => x.DeletedAt == null, token),
            await Rows<Product>(id).CountAsync(x => x.DeletedAt != null, token),
            await Rows<InventoryLot>(id).CountAsync(token), await Rows<InventoryItem>(id).CountAsync(token),
            await Rows<ProductImage>(id).CountAsync(token) + await Rows<InventoryItemImage>(id).CountAsync(token),
            await Rows<Work>(id).CountAsync(token) + await Rows<Series>(id).CountAsync(token) + await Rows<Character>(id).CountAsync(token)
                + await Rows<Tag>(id).CountAsync(token) + await Rows<Organization>(id).CountAsync(token) + await Rows<Category>(id).CountAsync(token));
    }

    /// <summary>最後の所属・確認時点・名称を再照合してから、全体削除を外側のトランザクションで確定する。</summary>
    public async Task DeleteAsync(CollectionSpace space, DeleteCollectionRequest request, DateTimeOffset now, CancellationToken token)
    {
        var impact = await ImpactAsync(space, token);
        if (!impact.CanDelete)
        {
            throw new MasterProblem(409, "LAST_COLLECTION_REQUIRED");
        }

        if (request.Name != space.Name || request.Confirmation != impact.Confirmation)
        {
            throw new MasterProblem(412, "COLLECTION_IMPACT_CHANGED");
        }

        var id = space.Id;
        var uploadIds = await Rows<TemporaryUpload>(id).Select(x => x.Id).ToArrayAsync(token);
        var alertKeys = uploadIds.Select(upload => "image-upload:" + upload.ToString("D")).ToArray();
        await db.Set<SystemAlert>().Where(x => x.ResolvedAt == null && alertKeys.Contains(x.AlertKey)).ExecuteUpdateAsync(set => set.SetProperty(x => x.ResolvedAt, now), token);

        // 変換中の処理も、公開前にアップロードの削除を確認して停止する。
        // トランザクションが取り消された場合に変換だけを中断しないよう、ここでは取消を送らない。
        await Remove<ProductImage>(id, token);
        await Remove<InventoryItemImage>(id, token);
        await Remove<ProductIdentifier>(id, token);
        await Remove<ProductLink>(id, token);
        await Remove<ProductWork>(id, token);
        await Remove<ProductCharacter>(id, token);
        await Remove<ProductTag>(id, token);
        await Remove<ProductSearchDocument>(id, token);
        await Remove<ProductChange>(id, token);
        await Remove<InventoryItem>(id, token);
        await Remove<InventoryLot>(id, token);
        await Remove<Product>(id, token);
        await Remove<CharacterWork>(id, token);
        await Remove<WorkSeries>(id, token);
        await Remove<TemporaryUpload>(id, token);
        await Remove<ImageAsset>(id, token);
        await Remove<ImageSettings>(id, token);
        await Rows<Category>(id).ExecuteUpdateAsync(set => set.SetProperty(x => x.ParentCategoryId, (Guid?)null), token);
        await Remove<Category>(id, token);
        await Remove<Work>(id, token);
        await Remove<Series>(id, token);
        await Remove<Character>(id, token);
        await Remove<Tag>(id, token);
        await Remove<Organization>(id, token);
        await Remove<IdempotencyRecord>(id, token);
        await db.Set<CollectionMember>().Where(x => x.CollectionId == id).ExecuteDeleteAsync(token);
        db.Remove(space);
    }

    /// <summary>HTTPの選択状態に依存せず、確認済みコレクションだけを照会する。</summary>
    private IQueryable<T> Rows<T>(Guid id) where T : class => db.Set<T>().IgnoreQueryFilters().Where(x => EF.Property<Guid>(x, "CollectionId") == id);

    /// <summary>対象所属の行だけを削除し、別のコレクションを保持する。</summary>
    private Task<int> Remove<T>(Guid id, CancellationToken token) where T : class => Rows<T>(id).ExecuteDeleteAsync(token);
}
