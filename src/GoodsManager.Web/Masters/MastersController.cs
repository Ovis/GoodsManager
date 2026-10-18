using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoodsManager.Web.Auth;
using GoodsManager.Web.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GoodsManager.Web.Masters;

/// <summary>認証・CSRFを共通基盤へ委ね、マスター変更と成功再送記録を同時に確定</summary>
[Authorize]
[Route("api/v1")]
public sealed partial class MastersController(AppDbContext db, SignInManager<AppUser> signIn, AuthWriteGate gate, TimeProvider clock) : ControllerBase
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [HttpGet("masters/{kind}")]
    public async Task<IActionResult> List(string kind, string? q = null, int page = 1, int pageSize = 50, [FromQuery] Guid[]? relatedWorkIds = null)
    {
        if (!ModelState.IsValid || page < 1 || pageSize is not (25 or 50 or 100) || !StorageValues.IsValidText(q ?? "", 0, 200)
            || relatedWorkIds?.Length > 100 || relatedWorkIds?.Contains(Guid.Empty) == true
            || relatedWorkIds?.Distinct().Count() != relatedWorkIds?.Length || (kind != "characters" && relatedWorkIds?.Length > 0)) throw MasterInput.Invalid();
        var search = MasterText.Normalize(q ?? "");
        // 複数SELECTを同一の読取スナップショットに固定。読取はSQLiteの書込予約を取らない。
        await db.Database.OpenConnectionAsync(HttpContext.RequestAborted);
        await using var native = ((SqliteConnection)db.Database.GetDbConnection()).BeginTransaction(deferred: true);
        await using var transaction = await db.Database.UseTransactionAsync(native, HttpContext.RequestAborted);
        var result = kind switch
        {
            "works" => await PageAsync<Work>(kind, search, page, pageSize, []),
            "series" => await PageAsync<Series>(kind, search, page, pageSize, []),
            "characters" => await PageAsync<Character>(kind, search, page, pageSize, relatedWorkIds ?? []),
            "tags" => await PageAsync<Tag>(kind, search, page, pageSize, []),
            "organizations" => await PageAsync<Organization>(kind, search, page, pageSize, []),
            "categories" => await PageAsync<Category>(kind, search, page, pageSize, []),
            _ => throw new MasterProblem(404, "NOT_FOUND")
        };
        await native.CommitAsync(HttpContext.RequestAborted);
        return Ok(result);
    }

    [HttpGet("categories/tree")]
    public async Task<IActionResult> Tree()
    {
        var categories = await db.Set<Category>().AsNoTracking().OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToListAsync(HttpContext.RequestAborted);
        return Ok(new { nodes = categories.Select(x => new { id = x.Id, parentId = x.ParentCategoryId, x.Name, x.SortOrder, revision = Number(x.Revision) }) });
    }

    [HttpPost("masters/{kind}")]
    [RequestSizeLimit(65536)]
    public Task<IActionResult> Create(string kind) => WriteAsync(kind, null);

    [HttpPut("masters/{kind}/{id:guid}")]
    [RequestSizeLimit(65536)]
    public Task<IActionResult> Update(string kind, Guid id) => WriteAsync(kind, id);

    [HttpGet("masters/{kind}/{id:guid}")]
    public async Task<IActionResult> Get(string kind, Guid id)
    {
        await db.Database.OpenConnectionAsync(HttpContext.RequestAborted);
        await using var native = ((SqliteConnection)db.Database.GetDbConnection()).BeginTransaction(deferred: true);
        await using var transaction = await db.Database.UseTransactionAsync(native, HttpContext.RequestAborted);
        INamedMaster? item = kind switch
        {
            "works" => await db.Set<Work>().FindAsync([id], HttpContext.RequestAborted),
            "series" => await db.Set<Series>().FindAsync([id], HttpContext.RequestAborted),
            "characters" => await db.Set<Character>().FindAsync([id], HttpContext.RequestAborted),
            "tags" => await db.Set<Tag>().FindAsync([id], HttpContext.RequestAborted),
            "organizations" => await db.Set<Organization>().FindAsync([id], HttpContext.RequestAborted),
            "categories" => await db.Set<Category>().FindAsync([id], HttpContext.RequestAborted),
            _ => null
        };
        if (item is null) throw new MasterProblem(404, "NOT_FOUND");
        var dto = Dto(kind, item);
        await native.CommitAsync(HttpContext.RequestAborted);
        Response.Headers.ETag = Etag(kind, item);
        return Ok(dto);
    }

    private async Task<object> PageAsync<T>(string kind, string search, int page, int size, Guid[] priority) where T : class, INamedMaster
    {
        var token = HttpContext.RequestAborted;
        var query = db.Set<T>().AsNoTracking().Where(x => x.NormalizedName.Contains(search));
        var count = await query.LongCountAsync(token);
        var offset = ((long)page - 1) * size;
        var selected = offset > int.MaxValue ? [] : await query
            .OrderByDescending(x => db.Set<CharacterWork>().Any(r => r.CharacterId == x.Id && priority.Contains(r.WorkId)))
            .ThenBy(x => x.NormalizedName).ThenBy(x => x.Id).Skip((int)offset).Take(size).ToListAsync(token);
        var items = new List<object>();
        foreach (var item in selected) items.Add(Dto(kind, item));
        var state = await db.SystemStates.AsNoTracking().SingleAsync(token);
        return new { items, page, pageSize = size, totalCount = count, totalPages = (count + size - 1) / size, dataRevision = Number(state.DataRevision) };
    }

    private async Task<IActionResult> WriteAsync(string kind, Guid? id, bool relations = false)
    {
        if (!Request.HasJsonContentType()) throw new MasterProblem(415, "UNSUPPORTED_MEDIA_TYPE");
        // Content-Lengthがない要求も、JSONの外側の空白も含めて上限を適用。
        var bytes = new byte[65537];
        var length = 0;
        while (length < bytes.Length)
        {
            var read = await Request.Body.ReadAsync(bytes.AsMemory(length), HttpContext.RequestAborted);
            if (read == 0) break;
            length += read;
        }
        if (length > 65536) throw new MasterProblem(413, "REQUEST_TOO_LARGE");
        if (!Guid.TryParseExact(Request.Headers["Idempotency-Key"].ToString(), "D", out var key) || key == Guid.Empty) throw MasterInput.Invalid();
        var scope = relations ? $"POST:/api/v1/masters/{kind}/{id:D}/relations" : id is null ? $"POST:/api/v1/masters/{kind}" : $"PUT:/api/v1/masters/{kind}/{id:D}";
        var condition = Request.Headers.IfMatch.ToString();
        var bodyHash = Convert.ToHexStringLower(SHA256.HashData(bytes.AsSpan(0, length)));
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[] { bodyHash, condition }))));
        var token = HttpContext.RequestAborted;
        await gate.Semaphore.WaitAsync(token);
        try
        {
            // EFのSQLiteトランザクションはBEGIN IMMEDIATE。失効確認から記録確定まで書込順序を固定。
            await using var transaction = await db.Database.BeginTransactionAsync(token);
            var user = await AuthenticationState.GetCurrentUserAsync(db, signIn, User);
            if (user is null) throw new MasterProblem(401, "SESSION_EXPIRED");
            var now = StorageValues.FromMilliseconds(StorageValues.ToMilliseconds(clock.GetUtcNow()));
            var previous = await db.Set<IdempotencyRecord>().SingleOrDefaultAsync(x => x.UserId == user.Id && x.OperationScope == scope && x.Key == key, token);
            if (previous is not null && previous.ExpiresAt > now)
            {
                if (previous.RequestHash != hash) throw new MasterProblem(409, "IDEMPOTENCY_KEY_REUSED");
                return StoredResponse(previous);
            }
            if (previous is not null) db.Entry(previous).State = EntityState.Detached;
            // 保持期限後の記録だけを削除。業務データの変更番号には含めない。
            await db.Set<IdempotencyRecord>().Where(x => x.ExpiresAt <= now).ExecuteDeleteAsync(token);
            JsonDocument document;
            try { document = JsonDocument.Parse(bytes.AsMemory(0, length)); }
            catch (JsonException) { throw MasterInput.Invalid(); }
            using var parsed = document;
            var body = document.RootElement;
            INamedMaster item;
            if (relations) item = await ChangeRelationsAsync(kind, id!.Value, body, now, condition);
            else
            {
                var input = MasterInput.Parse(kind, body, id.HasValue);
                item = kind switch
                {
                    "works" => await ChangeAsync<Work>(kind, id, input, now, condition),
                    "series" => await ChangeAsync<Series>(kind, id, input, now, condition),
                    "characters" => await ChangeAsync<Character>(kind, id, input, now, condition),
                    "tags" => await ChangeAsync<Tag>(kind, id, input, now, condition),
                    "organizations" => await ChangeAsync<Organization>(kind, id, input, now, condition),
                    "categories" => await ChangeAsync<Category>(kind, id, input, now, condition),
                    _ => throw new MasterProblem(404, "NOT_FOUND")
                };
            }
            await db.SaveChangesAsync(token);
            var completedAt = StorageValues.FromMilliseconds(StorageValues.ToMilliseconds(clock.GetUtcNow()));
            var response = new IdempotencyRecord
            {
                UserId = user.Id,
                OperationScope = scope,
                Key = key,
                RequestHash = hash,
                ResponseStatus = id.HasValue ? 200 : 201,
                ResponseJson = JsonSerializer.Serialize(Dto(kind, item), Json),
                ResponseEtag = Etag(kind, item),
                Location = id.HasValue ? null : $"/api/v1/masters/{kind}/{item.Id:D}",
                CompletedAt = completedAt,
                ExpiresAt = completedAt.AddDays(7)
            };
            db.Add(response);
            await db.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
            return StoredResponse(response);
        }
        finally { gate.Semaphore.Release(); }
    }

    private IActionResult StoredResponse(IdempotencyRecord record)
    {
        Response.Headers.ETag = record.ResponseEtag;
        if (record.Location is not null) Response.Headers.Location = record.Location;
        return new ContentResult { StatusCode = record.ResponseStatus, ContentType = "application/json; charset=utf-8", Content = record.ResponseJson };
    }

    private async Task<INamedMaster> ChangeAsync<T>(string kind, Guid? id, MasterInput input, DateTimeOffset now, string condition) where T : class, INamedMaster, new()
    {
        var token = HttpContext.RequestAborted;
        var item = id.HasValue ? await db.Set<T>().SingleOrDefaultAsync(x => x.Id == id.Value, token) ?? throw new MasterProblem(404, "NOT_FOUND") : new T { Id = Guid.NewGuid(), CreatedAt = now, UpdatedAt = now, Revision = 1 };
        if (id.HasValue)
        {
            CheckVersion(kind, item, condition);
        }
        var changed = !id.HasValue || item.Name != input.Name;
        if (item is Organization org)
        {
            changed |= org.WebsiteUrl != input.WebsiteUrl || org.Memo != input.Memo;
            org.WebsiteUrl = input.WebsiteUrl;
            org.Memo = input.Memo;
        }
        if (item is Category category)
        {
            await ValidateParentAsync(item.Id, input.ParentId);
            changed |= category.ParentCategoryId != input.ParentId || category.SortOrder != input.SortOrder;
            category.ParentCategoryId = input.ParentId;
            category.SortOrder = input.SortOrder;
        }
        item.Name = input.Name;
        item.NormalizedName = MasterText.Normalize(input.Name);
        if (!id.HasValue) db.Add(item);
        else if (changed) Touch(item, now);
        if (changed)
        {
            var state = await db.SystemStates.SingleAsync(token);
            state.DataRevision = checked(state.DataRevision + 1);
            state.UpdatedAt = now;
        }
        return item;
    }

    private async Task ValidateParentAsync(Guid id, Guid? parent)
    {
        if (!parent.HasValue) return;
        var parents = await db.Set<Category>().AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.ParentCategoryId, HttpContext.RequestAborted);
        if (!parents.ContainsKey(parent.Value)) throw new MasterProblem(400, "RELATED_NOT_FOUND");
        var visited = new HashSet<Guid> { id };
        while (parent.HasValue)
        {
            if (!visited.Add(parent.Value)) throw new MasterProblem(409, "CATEGORY_CYCLE");
            parent = parents[parent.Value];
        }
    }

    private static object Dto(string kind, INamedMaster item)
    {
        var result = new Dictionary<string, object?>
        {
            ["id"] = item.Id,
            ["name"] = item.Name,
            ["revision"] = Number(item.Revision),
            ["etag"] = Etag(kind, item),
            ["createdAt"] = item.CreatedAt,
            ["updatedAt"] = item.UpdatedAt,
            ["usage"] = new { activeProductCount = 0, trashedProductCount = 0 }
        };
        if (item is Organization org) { result["websiteUrl"] = org.WebsiteUrl; result["memo"] = org.Memo; }
        if (item is Category cat) { result["parentId"] = cat.ParentCategoryId; result["sortOrder"] = cat.SortOrder; }
        return result;
    }

    private static void CheckVersion(string kind, INamedMaster item, string condition)
    {
        if (string.IsNullOrEmpty(condition)) throw new MasterProblem(428, "PRECONDITION_REQUIRED");
        if (condition != Etag(kind, item)) throw new MasterProblem(412, "REVISION_MISMATCH");
    }

    private static void Touch(INamedMaster item, DateTimeOffset now) { item.Revision = checked(item.Revision + 1); item.UpdatedAt = now; }
    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
    private static string Etag(string kind, INamedMaster item) => $"\"master:{kind}:{item.Id:D}:{Number(item.Revision)}\"";
}
