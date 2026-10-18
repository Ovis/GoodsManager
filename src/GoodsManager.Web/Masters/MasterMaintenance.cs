using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GoodsManager.Web.Products;
using GoodsManager.Web.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoodsManager.Web.Masters;

public sealed partial class MastersController
{
    private sealed record ReferenceRole(string Name, string Table, string Column, string OwnerColumn, string? OwnerKind, bool Optional = false);
    private sealed record Reference(ReferenceRole Role, Guid Owner);
    private sealed record MaintenanceResult(INamedMaster Item, Guid[] Affected);

    /// <summary>統合元・先の属性と全参照を、署名や期限のない確認データへまとめる</summary>
    [HttpPost("masters/{kind}/merge-preview")]
    [RequestSizeLimit(65536)]
    public Task<IActionResult> MergePreview(string kind) => PreviewRead(async body =>
    {
        var input = new InputObject(body, "sourceId", "targetId");
        return await ImpactAsync(kind, input.Id("sourceId"), input.Id("targetId"), merge: true);
    });

    /// <summary>関連解除と付け替えの両方に、同じ全参照の確認を使う</summary>
    [HttpPost("masters/{kind}/{id:guid}/deletion-preview")]
    [RequestSizeLimit(65536)]
    public Task<IActionResult> DeletionPreview(string kind, Guid id) => PreviewRead(async body =>
    {
        var input = new InputObject(body, "replacementId");
        return await ImpactAsync(kind, id, input.OptionalId("replacementId"), merge: false);
    });

    /// <summary>確認後の変更を同一書込トランザクションで再評価して統合</summary>
    [HttpPost("masters/{kind}/merges")]
    [RequestSizeLimit(65536)]
    public Task<IActionResult> Merge(string kind) => WriteAsync(kind, null, maintenance: "merge");

    /// <summary>空カテゴリ、または確認した付け替え・関連解除だけを削除</summary>
    [HttpPost("masters/{kind}/{id:guid}/deletion")]
    [RequestSizeLimit(65536)]
    public Task<IActionResult> DeleteMaster(string kind, Guid id) => WriteAsync(kind, id, maintenance: "delete");

    /// <summary>未登録の種類・自己統合・カテゴリ統合を拒否して正本を取得</summary>
    private async Task<INamedMaster> NamedAsync(string kind, Guid id)
    {
        INamedMaster? value = kind switch
        {
            "works" => await db.Set<Work>().FindAsync(id),
            "series" => await db.Set<Series>().FindAsync(id),
            "characters" => await db.Set<Character>().FindAsync(id),
            "tags" => await db.Set<Tag>().FindAsync(id),
            "organizations" => await db.Set<Organization>().FindAsync(id),
            "categories" => await db.Set<Category>().FindAsync(id),
            _ => null
        };
        return value ?? throw new MasterProblem(404, "NOT_FOUND");
    }

    /// <summary>テーブル・列は固定の許可リスト。SQL構造へ利用者入力を連結しない</summary>
    private static ReferenceRole[] Roles(string kind) => kind switch
    {
        "works" => [new("products", "ProductWorks", "WorkId", "ProductId", null), new("series", "WorkSeries", "WorkId", "SeriesId", "series"), new("characters", "CharacterWorks", "WorkId", "CharacterId", "characters")],
        "series" => [new("works", "WorkSeries", "SeriesId", "WorkId", "works")],
        "characters" => [new("products", "ProductCharacters", "CharacterId", "ProductId", null), new("works", "CharacterWorks", "CharacterId", "WorkId", "works")],
        "tags" => [new("products", "ProductTags", "TagId", "ProductId", null)],
        "organizations" => [new("manufacturer", "Products", "ManufacturerId", "ProductId", null, true), new("seller", "Products", "SellerId", "ProductId", null, true)],
        "categories" => [new("products", "Products", "CategoryId", "ProductId", null), new("children", "Categories", "ParentCategoryId", "CategoryId", "categories")],
        _ => throw new MasterProblem(404, "NOT_FOUND")
    };

    /// <summary>業務トランザクションを共有する、固定SQLのコマンド</summary>
    private DbCommand ReferenceCommand(string sql, Guid source, Guid? target = null)
    {
        var command = db.Database.GetDbConnection().CreateCommand(); command.Transaction = db.Database.CurrentTransaction!.GetDbTransaction(); command.CommandText = sql;
        command.Parameters.Add(new SqliteParameter("@source", source.ToString("D"))); command.Parameters.Add(new SqliteParameter("@target", target.HasValue ? target.Value.ToString("D") : DBNull.Value)); return command;
    }

    /// <summary>ゴミ箱商品も含む全参照を読み、確認件数が同じでも対象入替を検出</summary>
    private async Task<Reference[]> ReferencesAsync(string kind, Guid id)
    {
        var references = new List<Reference>();
        foreach (var role in Roles(kind))
        {
            await using var command = ReferenceCommand($"SELECT {role.OwnerColumn} FROM {role.Table} WHERE {role.Column}=@source ORDER BY {role.OwnerColumn}", id);
            await using var reader = await command.ExecuteReaderAsync(HttpContext.RequestAborted);
            while (await reader.ReadAsync(HttpContext.RequestAborted)) references.Add(new(role, StorageValues.ParseId(reader.GetString(0))));
        }
        return references.ToArray();
    }

    /// <summary>確認画面用の件数・失われる属性と、全参照の内容比較値を返す</summary>
    private async Task<object> ImpactAsync(string kind, Guid sourceId, Guid? targetId, bool merge)
    {
        if (merge && kind == "categories") throw new MasterProblem(404, "NOT_FOUND");
        if (sourceId == targetId || kind == "categories" && targetId.HasValue) throw MasterInput.Invalid("targetId");
        var source = await NamedAsync(kind, sourceId); var target = targetId.HasValue ? await NamedAsync(kind, targetId.Value) : null;
        var refs = await ReferencesAsync(kind, sourceId); var targetRefs = target is null ? [] : await ReferencesAsync(kind, target.Id);
        var ids = kind == "categories" ? refs.Where(x => x.Role.Name == "products").Select(x => x.Owner).ToArray() : await projection.AffectedAsync(kind, sourceId, HttpContext.RequestAborted);
        var products = await db.Set<Product>().AsNoTracking().Where(x => ids.Contains(x.Id)).OrderBy(x => x.Id).Select(x => new { x.Id, x.Revision, x.DeletedAt }).ToArrayAsync(HttpContext.RequestAborted);
        var related = new List<object>();
        foreach (var reference in refs.Where(x => x.Role.OwnerKind is not null))
        {
            var peer = await NamedAsync(reference.Role.OwnerKind!, reference.Owner); related.Add(new { kind = reference.Role.OwnerKind, peer.Id, peer.Name, revision = Number(peer.Revision) });
        }
        var sourceDto = await Dto(kind, source); var targetDto = target is null ? null : await Dto(kind, target);
        var fingerprint = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { sourceDto, targetDto, references = refs.Select(x => new { role = x.Role.Name, x.Owner }), targetReferences = targetRefs.Select(x => new { role = x.Role.Name, x.Owner }), products, related }, Json))));
        var impact = new { sourceId, targetId, sourceRevision = Number(source.Revision), targetRevision = target is null ? null : Number(target.Revision), activeProductCount = products.LongCount(x => x.DeletedAt is null), trashedProductCount = products.LongCount(x => x.DeletedAt is not null), referenceCounts = refs.GroupBy(x => x.Role.Name).ToDictionary(x => x.Key, x => x.LongCount()), duplicateRelationCount = refs.LongCount(x => targetRefs.Any(t => t.Role.Name == x.Role.Name && t.Owner == x.Owner)), referenceFingerprint = fingerprint };
        return new { source = sourceDto, target = targetDto, confirmedImpact = impact, affectedMasters = related, lostAttributes = new { source.Name, websiteUrl = (source as Organization)?.WebsiteUrl, memo = (source as Organization)?.Memo }, canDelete = kind != "categories" || refs.Length == 0 };
    }

    /// <summary>全参照を再評価し、件数減少や同数の入替も利用者へ再提示</summary>
    private async Task<MaintenanceResult> MaintainAsync(string kind, Guid? id, JsonElement body, DateTimeOffset now, string condition, bool merge)
    {
        var input = merge ? new InputObject(body, "sourceId", "targetId", "confirmedImpact") : new InputObject(body, "replacementId", "detachConfirmed", "confirmedImpact");
        var sourceId = merge ? input.Id("sourceId") : id!.Value; var targetId = merge ? input.Id("targetId") : input.OptionalId("replacementId");
        var source = await NamedAsync(kind, sourceId); if (!merge) CheckVersion(kind, source, condition);
        var latest = await ImpactAsync(kind, sourceId, targetId, merge);
        var latestJson = JsonSerializer.SerializeToElement(latest, Json);
        if (!JsonNode.DeepEquals(JsonNode.Parse(input.Get("confirmedImpact").GetRawText()), JsonNode.Parse(latestJson.GetProperty("confirmedImpact").GetRawText()))) throw new MasterProblem(409, "MASTER_IMPACT_CHANGED") { Details = latest };
        var refs = await ReferencesAsync(kind, sourceId);
        if (kind == "categories" && refs.Length > 0) throw new MasterProblem(409, "MASTER_IN_USE") { Details = latest };
        if (!merge && targetId is null && refs.Length > 0 && (!body.TryGetProperty("detachConfirmed", out var ack) || ack.ValueKind != JsonValueKind.True)) throw MasterInput.Invalid("detachConfirmed");
        if (body.TryGetProperty("detachConfirmed", out var detach) && (detach.ValueKind is not (JsonValueKind.True or JsonValueKind.False) || detach.ValueKind == JsonValueKind.True && (targetId.HasValue || kind == "categories"))) throw MasterInput.Invalid("detachConfirmed");
        var target = targetId.HasValue ? await NamedAsync(kind, targetId.Value) : null;
        var affected = await projection.AffectedAsync(kind, sourceId, HttpContext.RequestAborted);
        foreach (var role in Roles(kind).Where(_ => kind != "categories"))
        {
            if (role.Optional)
            {
                await using var command = ReferenceCommand($"UPDATE {role.Table} SET {role.Column}=@target WHERE {role.Column}=@source", sourceId, targetId); await command.ExecuteNonQueryAsync(HttpContext.RequestAborted);
            }
            else
            {
                if (targetId.HasValue) { await using var insert = ReferenceCommand($"INSERT OR IGNORE INTO {role.Table} ({role.OwnerColumn},{role.Column}) SELECT {role.OwnerColumn},@target FROM {role.Table} WHERE {role.Column}=@source", sourceId, targetId); await insert.ExecuteNonQueryAsync(HttpContext.RequestAborted); }
                await using var remove = ReferenceCommand($"DELETE FROM {role.Table} WHERE {role.Column}=@source", sourceId); await remove.ExecuteNonQueryAsync(HttpContext.RequestAborted);
            }
        }
        // 関係を共有する側の更新条件も失効させ、古い全体置換・差分編集との衝突を見逃さない。
        foreach (var reference in refs.Where(x => x.Role.OwnerKind is not null && (kind == "works" || kind == "series"))) Touch(await NamedAsync(reference.Role.OwnerKind!, reference.Owner), now);
        if (target is not null) Touch(target, now);
        foreach (var product in await db.Set<Product>().Where(x => affected.Contains(x.Id)).ToArrayAsync(HttpContext.RequestAborted))
        {
            product.Revision = checked(product.Revision + 1); product.UpdatedAt = now;
            db.Add(new ProductChange { Id = Guid.NewGuid(), ProductId = product.Id, Revision = product.Revision, Kind = "updated", OccurredAt = now });
        }
        db.Remove(source); var state = await db.SystemStates.SingleAsync(HttpContext.RequestAborted); state.DataRevision = checked(state.DataRevision + 1); state.UpdatedAt = now;
        return new(target ?? source, affected);
    }

    /// <summary>確認の読取には書込予約を取らず、属性・参照のスナップショットを固定</summary>
    private async Task<IActionResult> PreviewRead(Func<JsonElement, Task<object>> read)
    {
        if (!Request.HasJsonContentType()) throw new MasterProblem(415, "UNSUPPORTED_MEDIA_TYPE");
        var bytes = new byte[65537]; var length = 0; while (length < bytes.Length) { var count = await Request.Body.ReadAsync(bytes.AsMemory(length), HttpContext.RequestAborted); if (count == 0) break; length += count; }
        if (length > 65536) throw new MasterProblem(413, "REQUEST_TOO_LARGE");
        using var document = ProductWrites.Parse(() => JsonDocument.Parse(bytes.AsMemory(0, length)));
        await db.Database.OpenConnectionAsync(HttpContext.RequestAborted);
        await using var native = ((SqliteConnection)db.Database.GetDbConnection()).BeginTransaction(deferred: true); await using var transaction = await db.Database.UseTransactionAsync(native, HttpContext.RequestAborted);
        var value = await read(document.RootElement); await native.CommitAsync(HttpContext.RequestAborted); return Ok(value);
    }
}
