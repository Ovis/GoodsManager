using GoodsManager.Web.Auth;
using GoodsManager.Web.Images;
using GoodsManager.Web.Masters;
using GoodsManager.Web.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GoodsManager.Web.Collections;

/// <summary>空・設定コピー・サンプルから新しいコレクションを作る入力。</summary>
public sealed record CreateCollectionRequest(string Name, Guid? ImportFrom = null, bool WithSamples = false);

/// <summary>商品などの内容を変更せず、コレクション名だけを変更する入力。</summary>
public sealed record RenameCollectionRequest(string Name);

/// <summary>件数の確認時点とコレクション名を照合して、全体削除を明示する入力。</summary>
public sealed record DeleteCollectionRequest(string Name, string Confirmation);

/// <summary>所属の作成・設定コピー・名前変更・全体削除を管理する。</summary>
[Authorize]
[ApiController]
[Route("api/v1/collections")]
public sealed class CollectionsController(AppDbContext db, SignInManager<AppUser> signIn, CollectionWrites writes, CollectionDeletion deletion) : ControllerBase
{
    /// <summary>認証済みアカウントの所属だけを照会する。</summary>
    private Guid UserId => Guid.Parse(signIn.UserManager.GetUserId(User)!);

    /// <summary>参加中のコレクションと、サンプル準備状態を返す。</summary>
    [HttpGet]
    public async Task<IActionResult> List()
    {
        var rows = await (from member in db.Set<CollectionMember>().AsNoTracking() join space in db.Set<CollectionSpace>().AsNoTracking() on member.CollectionId equals space.Id where member.UserId == UserId orderby space.CreatedAt, space.Id select new { Space = space, member.Role }).ToArrayAsync(HttpContext.RequestAborted);
        return Ok(new
        {
            items = rows.Select(x => CollectionWrites.View(x.Space, x.Role))
        });
    }

    /// <summary>サンプル投入は独立した準備処理へ渡し、通常の作成要求で待ち続けない。</summary>
    [HttpPost]
    [RequestSizeLimit(65536)]
    public Task<IActionResult> Create(CreateCollectionRequest request)
    {
        if (!StorageValues.IsValidText(request.Name, 1, 200) || string.IsNullOrWhiteSpace(request.Name) || request.ImportFrom == Guid.Empty || request.WithSamples && request.ImportFrom.HasValue)
        {
            throw MasterInput.Invalid();
        }

        return writes.ExecuteAsync(HttpContext, "POST:/api/v1/collections", request, 201, async (user, now, token) =>
        {
            if (request.ImportFrom.HasValue && !await db.Set<CollectionMember>().AnyAsync(x => x.CollectionId == request.ImportFrom && x.UserId == user.Id, token))
            {
                throw new MasterProblem(404, "COLLECTION_NOT_FOUND");
            }

            if (request.ImportFrom.HasValue && await db.Set<CollectionSpace>().AnyAsync(x => x.Id == request.ImportFrom && (x.SampleStatus == "pending" || x.SampleStatus == "failed"), token))
            {
                throw new MasterProblem(409, "SAMPLE_COLLECTION_NOT_READY");
            }

            var space = new CollectionSpace { Id = Guid.NewGuid(), Name = request.Name, SampleStatus = request.WithSamples ? "pending" : null, CreatedAt = now, UpdatedAt = now };
            db.Add(space);
            db.Add(new CollectionMember { CollectionId = space.Id, UserId = user.Id, Role = "manager" });
            if (request.ImportFrom.HasValue)
            {
                await Import(request.ImportFrom.Value, space.Id, now, token);
            }
            else
            {
                Add(new ImageSettings { CreatedAt = now, UpdatedAt = now }, space.Id);
            }

            return space;
        });
    }

    /// <summary>管理権限と取得済み更新条件を確認して名前を変更する。</summary>
    [HttpPut("{id:guid}")]
    [RequestSizeLimit(65536)]
    public Task<IActionResult> Rename(Guid id, RenameCollectionRequest request)
    {
        if (!StorageValues.IsValidText(request.Name, 1, 200) || string.IsNullOrWhiteSpace(request.Name))
        {
            throw MasterInput.Invalid("name");
        }

        return writes.ExecuteAsync(HttpContext, $"PUT:/api/v1/collections/{id:D}", request, 200, async (user, now, token) =>
        {
            var space = await ManagedAsync(id, user.Id, token);
            CollectionWrites.CheckVersion(space, Request.Headers.IfMatch.ToString());
            space.Name = request.Name;
            space.Revision = checked(space.Revision + 1);
            space.UpdatedAt = now;
            return space;
        });
    }

    /// <summary>失敗した専用コレクションだけを再び準備待ちにする。</summary>
    [HttpPost("{id:guid}/samples/retry")]
    [RequestSizeLimit(65536)]
    public Task<IActionResult> RetrySamples(Guid id) => writes.ExecuteAsync(HttpContext, $"POST:/api/v1/collections/{id:D}/samples/retry", new
    {
    }, 200, async (user, now, token) =>
    {
        var space = await ManagedAsync(id, user.Id, token);
        CollectionWrites.CheckVersion(space, Request.Headers.IfMatch.ToString());
        if (space.SampleStatus != "failed")
        {
            throw new MasterProblem(409, "SAMPLE_RETRY_NOT_AVAILABLE");
        }

        space.SampleStatus = "pending";
        space.Revision = checked(space.Revision + 1);
        space.UpdatedAt = now;
        return space;
    });

    /// <summary>削除する内容と、最後の所属を失う利用者がいないことを確認する。</summary>
    [HttpGet("{id:guid}/delete-impact")]
    public async Task<IActionResult> DeleteImpact(Guid id)
    {
        var token = HttpContext.RequestAborted;
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var space = await ManagedAsync(id, UserId, token);
        var result = await deletion.ImpactAsync(space, token);
        await transaction.CommitAsync(token);
        return Ok(result);
    }

    /// <summary>確認した全データを削除し、同じ要求の再送にも確定済みの結果を返す。</summary>
    [HttpDelete("{id:guid}")]
    [RequestSizeLimit(65536)]
    public Task<IActionResult> Delete(Guid id, DeleteCollectionRequest request) => writes.ExecuteAsync(HttpContext, $"DELETE:/api/v1/collections/{id:D}", request, 200, async (user, now, token) =>
    {
        var space = await ManagedAsync(id, user.Id, token);
        CollectionWrites.CheckVersion(space, Request.Headers.IfMatch.ToString());
        await deletion.DeleteAsync(space, request, now, token);
        return space;
    });

    /// <summary>非参加者へ存在を公開せず、参加者の管理権限を確認する。</summary>
    private async Task<CollectionSpace> ManagedAsync(Guid id, Guid userId, CancellationToken token)
    {
        var member = await db.Set<CollectionMember>().AsNoTracking().SingleOrDefaultAsync(x => x.CollectionId == id && x.UserId == userId, token) ?? throw new MasterProblem(404, "COLLECTION_NOT_FOUND");
        if (member.Role != "manager")
        {
            throw new MasterProblem(403, "COLLECTION_MANAGER_REQUIRED");
        }

        return await db.Set<CollectionSpace>().SingleAsync(x => x.Id == id, token);
    }
    /// <summary>設定コピーと新規設定へ、コピー先の所属を明示する。</summary>
    private void Add(object value, Guid destination)
    {
        db.Add(value);
        db.Entry(value).Property("CollectionId").CurrentValue = destination;
    }

    /// <summary>マスター階層・関連・画像設定だけをコピーし、商品と画像を持ち込まない。</summary>
    private async Task Import(Guid source, Guid destination, DateTimeOffset now, CancellationToken token)
    {
        // 新しいIDへ対応付けてから、親子関係とマスター間関連を復元する。
        async Task<Dictionary<Guid, Guid>> Copy<T>() where T : class, INamedMaster, new()
        {
            var rows = await db.Set<T>().AsNoTracking().Where(x => EF.Property<Guid>(x, "CollectionId") == source).ToArrayAsync(token);
            var map = rows.ToDictionary(x => x.Id, _ => Guid.NewGuid());
            foreach (var row in rows)
            {
                var copy = new T { Id = map[row.Id], Name = row.Name, NormalizedName = row.NormalizedName, Revision = 1, CreatedAt = now, UpdatedAt = now };
                if (row is Organization org && copy is Organization target)
                {
                    target.WebsiteUrl = org.WebsiteUrl;
                    target.Memo = org.Memo;
                }

                if (row is Category cat && copy is Category category)
                {
                    category.SortOrder = cat.SortOrder;
                    category.ParentCategoryId = cat.ParentCategoryId.HasValue ? map[cat.ParentCategoryId.Value] : null;
                }

                Add(copy, destination);
            }

            return map;
        }

        var works = await Copy<Work>();
        var series = await Copy<Series>();
        var characters = await Copy<Character>();
        await Copy<Tag>();
        await Copy<Organization>();
        await Copy<Category>();
        foreach (var row in await db.Set<WorkSeries>().AsNoTracking().Where(x => EF.Property<Guid>(x, "CollectionId") == source).ToArrayAsync(token))
        {
            Add(new WorkSeries { WorkId = works[row.WorkId], SeriesId = series[row.SeriesId] }, destination);
        }

        foreach (var row in await db.Set<CharacterWork>().AsNoTracking().Where(x => EF.Property<Guid>(x, "CollectionId") == source).ToArrayAsync(token))
        {
            Add(new CharacterWork { CharacterId = characters[row.CharacterId], WorkId = works[row.WorkId] }, destination);
        }

        var settings = await db.Set<ImageSettings>().AsNoTracking().SingleAsync(x => EF.Property<Guid>(x, "CollectionId") == source, token);
        Add(new ImageSettings { KeepOriginal = settings.KeepOriginal, DisplayMaxEdge = settings.DisplayMaxEdge, DisplayQuality = settings.DisplayQuality, ThumbMaxEdge = settings.ThumbMaxEdge, ThumbQuality = settings.ThumbQuality, CreatedAt = now, UpdatedAt = now }, destination);
    }
}
