using System.Text.Json;
using GoodsManager.Web.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GoodsManager.Web.Masters;

/// <summary>関連の保存件数は制限せず、一要求の差分と読取ページだけを制限</summary>
public sealed record RelationInput(Guid[] AddIds, Guid[] RemoveIds)
{
    public static RelationInput Parse(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Object) throw MasterInput.Invalid();
        var fields = json.EnumerateObject().Select(x => x.Name).ToArray();
        if (fields.Length != 2 || !fields.Contains("addIds") || !fields.Contains("removeIds")) throw MasterInput.Invalid();
        var add = ReadArray(json.GetProperty("addIds"), "addIds");
        var remove = ReadArray(json.GetProperty("removeIds"), "removeIds");
        if (add.Intersect(remove).Any()) throw MasterInput.Invalid("removeIds");
        return new(add, remove);
    }

    private static Guid[] ReadArray(JsonElement json, string field)
    {
        if (json.ValueKind != JsonValueKind.Array || json.GetArrayLength() > 100) throw MasterInput.Invalid(field);
        var ids = json.EnumerateArray().Select(MasterInput.ReadId).ToArray();
        if (ids.Distinct().Count() != ids.Length) throw MasterInput.Invalid(field);
        return ids;
    }
}

public sealed partial class MastersController
{
    /// <summary>作品→シリーズ、シリーズ→作品、キャラクター→作品の関連一覧</summary>
    [HttpGet("masters/{kind}/{id:guid}/relations")]
    public async Task<IActionResult> Relations(string kind, Guid id, int page = 1, int pageSize = 50)
    {
        if (!ModelState.IsValid || page < 1 || pageSize is not (25 or 50 or 100)) throw MasterInput.Invalid();
        var token = HttpContext.RequestAborted;
        await db.Database.OpenConnectionAsync(token);
        await using var native = ((SqliteConnection)db.Database.GetDbConnection()).BeginTransaction(deferred: true);
        await using var transaction = await db.Database.UseTransactionAsync(native, token);
        var owner = await RelationOwnerAsync(kind, id);
        var result = kind switch
        {
            "works" => await RelationPageAsync(db.Set<Series>().Where(x => db.Set<WorkSeries>().Any(r => r.WorkId == id && r.SeriesId == x.Id)), "series", owner, kind, page, pageSize),
            "series" => await RelationPageAsync(db.Set<Work>().Where(x => db.Set<WorkSeries>().Any(r => r.SeriesId == id && r.WorkId == x.Id)), "works", owner, kind, page, pageSize),
            "characters" => await RelationPageAsync(db.Set<Work>().Where(x => db.Set<CharacterWork>().Any(r => r.CharacterId == id && r.WorkId == x.Id)), "works", owner, kind, page, pageSize),
            _ => throw new MasterProblem(404, "NOT_FOUND")
        };
        await native.CommitAsync(token);
        Response.Headers.ETag = Etag(kind, owner);
        return Ok(result);
    }

    /// <summary>明示した追加・解除だけを適用。追加済み/解除済みへの同じ指定は無変更</summary>
    [HttpPost("masters/{kind}/{id:guid}/relations")]
    [RequestSizeLimit(65536)]
    public Task<IActionResult> ChangeRelations(string kind, Guid id) => WriteAsync(kind, id, relations: true);

    private async Task<object> RelationPageAsync<T>(IQueryable<T> query, string peerKind, INamedMaster owner, string kind, int page, int pageSize) where T : class, INamedMaster
    {
        var token = HttpContext.RequestAborted;
        var count = await query.LongCountAsync(token);
        var offset = ((long)page - 1) * pageSize;
        var selected = offset > int.MaxValue ? [] : await query.AsNoTracking().OrderBy(x => x.NormalizedName).ThenBy(x => x.Id).Skip((int)offset).Take(pageSize).ToListAsync(token);
        var state = await db.SystemStates.AsNoTracking().SingleAsync(token);
        return new
        {
            items = await RelationDtosAsync(peerKind, selected),
            page,
            pageSize,
            totalCount = count,
            totalPages = (count + pageSize - 1) / pageSize,
            masterRevision = Number(owner.Revision),
            masterEtag = Etag(kind, owner),
            dataRevision = Number(state.DataRevision)
        };
    }

    private async Task<object[]> RelationDtosAsync<T>(string kind, IEnumerable<T> selected) where T : class, INamedMaster
    {
        var items = new List<object>();
        foreach (var item in selected) items.Add(await Dto(kind, item));
        return items.ToArray();
    }
    private async Task<INamedMaster> RelationOwnerAsync(string kind, Guid id)
    {
        INamedMaster? owner = kind switch
        {
            "works" => await db.Set<Work>().FindAsync([id], HttpContext.RequestAborted),
            "series" => await db.Set<Series>().FindAsync([id], HttpContext.RequestAborted),
            "characters" => await db.Set<Character>().FindAsync([id], HttpContext.RequestAborted),
            _ => null
        };
        return owner ?? throw new MasterProblem(404, "NOT_FOUND");
    }

    private async Task<INamedMaster> ChangeRelationsAsync(string kind, Guid id, JsonElement body, DateTimeOffset now, string condition)
    {
        var item = await RelationOwnerAsync(kind, id);
        CheckVersion(kind, item, condition);
        var input = RelationInput.Parse(body);
        var ids = input.AddIds.Concat(input.RemoveIds).ToArray();
        var token = HttpContext.RequestAborted;
        var changed = false;
        if (item is Work or Series)
        {
            var workSide = item is Work;
            // 全関連を読み込まず、要求された最大200件に絞って差分を評価。
            var existing = await db.Set<WorkSeries>().Where(x => workSide
                ? x.WorkId == id && ids.Contains(x.SeriesId)
                : x.SeriesId == id && ids.Contains(x.WorkId)).ToListAsync(token);
            var old = existing.Select(x => workSide ? x.SeriesId : x.WorkId).ToHashSet();
            var added = input.AddIds.Except(old).ToArray();
            var removed = input.RemoveIds.Intersect(old).ToArray();
            var affected = added.Concat(removed).ToHashSet();
            if (workSide)
            {
                var peers = await db.Set<Series>().Where(x => ids.Contains(x.Id)).ToListAsync(token);
                if (peers.Count != ids.Length) throw new MasterProblem(400, "RELATED_NOT_FOUND", "addIds/removeIds");
                foreach (var peer in peers.Where(x => affected.Contains(x.Id))) Touch(peer, now);
            }
            else
            {
                var peers = await db.Set<Work>().Where(x => ids.Contains(x.Id)).ToListAsync(token);
                if (peers.Count != ids.Length) throw new MasterProblem(400, "RELATED_NOT_FOUND", "addIds/removeIds");
                foreach (var peer in peers.Where(x => affected.Contains(x.Id))) Touch(peer, now);
            }
            db.RemoveRange(existing.Where(x => removed.Contains(workSide ? x.SeriesId : x.WorkId)));
            foreach (var peer in added) db.Add(new WorkSeries { WorkId = workSide ? id : peer, SeriesId = workSide ? peer : id });
            changed = affected.Count > 0;
        }
        else if (item is Character)
        {
            if (await db.Set<Work>().CountAsync(x => ids.Contains(x.Id), token) != ids.Length) throw new MasterProblem(400, "RELATED_NOT_FOUND", "addIds/removeIds");
            var existing = await db.Set<CharacterWork>().Where(x => x.CharacterId == id && ids.Contains(x.WorkId)).ToListAsync(token);
            var old = existing.Select(x => x.WorkId).ToHashSet();
            var added = input.AddIds.Except(old).ToArray();
            var removed = existing.Where(x => input.RemoveIds.Contains(x.WorkId)).ToArray();
            db.RemoveRange(removed);
            foreach (var peer in added) db.Add(new CharacterWork { CharacterId = id, WorkId = peer });
            changed = added.Length > 0 || removed.Length > 0;
        }
        if (changed)
        {
            Touch(item, now);
            var state = await db.SystemStates.SingleAsync(token);
            state.DataRevision = checked(state.DataRevision + 1);
            state.UpdatedAt = now;
        }
        return item;
    }
}
