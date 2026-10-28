using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoodsManager.Web.Auth;
using GoodsManager.Web.Images;
using GoodsManager.Web.Masters;
using GoodsManager.Web.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GoodsManager.Web.Collections;

public sealed record CreateCollectionRequest(string Name, Guid? ImportFrom = null);
public sealed record RenameCollectionRequest(string Name);

[Authorize]
[ApiController]
[Route("api/v1/collections")]
public sealed class CollectionsController(AppDbContext db, SignInManager<AppUser> signIn, AuthWriteGate gate, TimeProvider clock) : ControllerBase
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private Guid UserId => Guid.Parse(signIn.UserManager.GetUserId(User)!);
    private static string Etag(CollectionSpace space) => $"\"collection:{space.Id:D}:{space.Revision}\"";
    private static object View(CollectionSpace space, string role) => new { space.Id, space.Name, role, revision = space.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture), etag = Etag(space) };
    [HttpGet]
    public async Task<IActionResult> List()
    {
        var rows = await (from member in db.Set<CollectionMember>().AsNoTracking() join space in db.Set<CollectionSpace>().AsNoTracking() on member.CollectionId equals space.Id where member.UserId == UserId orderby space.CreatedAt, space.Id select new { Space = space, member.Role }).ToArrayAsync(HttpContext.RequestAborted);
        return Ok(new { items = rows.Select(x => View(x.Space, x.Role)) });
    }
    [HttpPost]
    [RequestSizeLimit(65536)]
    public Task<IActionResult> Create(CreateCollectionRequest request) => Write(null, request.Name, request.ImportFrom);
    [HttpPut("{id:guid}")]
    [RequestSizeLimit(65536)]
    public Task<IActionResult> Rename(Guid id, RenameCollectionRequest request) => Write(id, request.Name, null);
    private async Task<IActionResult> Write(Guid? id, string name, Guid? source)
    {
        if (!StorageValues.IsValidText(name, 1, 200) || string.IsNullOrWhiteSpace(name) || source == Guid.Empty || !Guid.TryParseExact(Request.Headers["Idempotency-Key"], "D", out var key) || key == Guid.Empty) throw MasterInput.Invalid();
        var token = HttpContext.RequestAborted;
        await gate.Semaphore.WaitAsync(token);
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(token);
            var user = await AuthenticationState.GetCurrentUserAsync(db, signIn, User) ?? throw new MasterProblem(401, "SESSION_EXPIRED");
            if (id.HasValue && !await db.Set<CollectionMember>().AnyAsync(x => x.CollectionId == id && x.UserId == user.Id && x.Role == "manager", token)) throw new MasterProblem(403, "COLLECTION_MANAGER_REQUIRED");
            if (source.HasValue && !await db.Set<CollectionMember>().AnyAsync(x => x.CollectionId == source && x.UserId == user.Id, token)) throw new MasterProblem(404, "COLLECTION_NOT_FOUND");
            var scope = id.HasValue ? $"PUT:/api/v1/collections/{id:D}" : "POST:/api/v1/collections";
            var condition = Request.Headers.IfMatch.ToString();
            var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { name, source, condition }))));
            var now = clock.GetUtcNow();
            var prior = await db.Set<IdempotencyRecord>().SingleOrDefaultAsync(x => x.UserId == user.Id && x.OperationScope == scope && x.Key == key && EF.Property<Guid>(x, "CollectionId") == CollectionSchema.DefaultId, token);
            if (prior is not null && prior.ExpiresAt > now)
            {
                if (prior.RequestHash != hash) throw new MasterProblem(409, "IDEMPOTENCY_KEY_REUSED");
                Response.Headers.ETag = prior.ResponseEtag; return new ContentResult { StatusCode = prior.ResponseStatus, ContentType = "application/json", Content = prior.ResponseJson };
            }
            if (prior is not null)
            {
                await db.Set<IdempotencyRecord>().Where(x => x.UserId == user.Id && x.OperationScope == scope && x.Key == key && EF.Property<Guid>(x, "CollectionId") == CollectionSchema.DefaultId).ExecuteDeleteAsync(token);
                db.Entry(prior).State = EntityState.Detached;
            }
            CollectionSpace space;
            if (id.HasValue)
            {
                space = await db.Set<CollectionSpace>().SingleAsync(x => x.Id == id, token);
                if (condition.Length == 0) throw new MasterProblem(428, "PRECONDITION_REQUIRED");
                if (condition != Etag(space)) throw new MasterProblem(412, "REVISION_MISMATCH");
                space.Name = name; space.Revision = checked(space.Revision + 1); space.UpdatedAt = now;
            }
            else
            {
                space = new CollectionSpace { Id = Guid.NewGuid(), Name = name, CreatedAt = now, UpdatedAt = now };
                db.Add(space); db.Add(new CollectionMember { CollectionId = space.Id, UserId = user.Id, Role = "manager" });
                if (source.HasValue) await Import(source.Value, space.Id, now, token);
                else Add(new ImageSettings { CreatedAt = now, UpdatedAt = now }, space.Id);
            }
            var response = JsonSerializer.Serialize(View(space, "manager"), Json);
            db.Add(new IdempotencyRecord { UserId = user.Id, OperationScope = scope, Key = key, RequestHash = hash, ResponseStatus = id.HasValue ? 200 : 201, ResponseJson = response, ResponseEtag = Etag(space), CompletedAt = now, ExpiresAt = now.AddDays(7) });
            var state = await db.SystemStates.SingleAsync(token); state.DataRevision = checked(state.DataRevision + 1); state.UpdatedAt = now;
            await db.SaveChangesAsync(token); await transaction.CommitAsync(token);
            Response.Headers.ETag = Etag(space); return new ContentResult { StatusCode = id.HasValue ? 200 : 201, ContentType = "application/json", Content = response };
        }
        finally { gate.Semaphore.Release(); }
    }
    private void Add(object value, Guid destination) { db.Add(value); db.Entry(value).Property("CollectionId").CurrentValue = destination; }
    private async Task Import(Guid source, Guid destination, DateTimeOffset now, CancellationToken token)
    {
        async Task<Dictionary<Guid, Guid>> Copy<T>() where T : class, INamedMaster, new()
        {
            var rows = await db.Set<T>().AsNoTracking().Where(x => EF.Property<Guid>(x, "CollectionId") == source).ToArrayAsync(token);
            var map = rows.ToDictionary(x => x.Id, _ => Guid.NewGuid());
            foreach (var row in rows)
            {
                var copy = new T { Id = map[row.Id], Name = row.Name, NormalizedName = row.NormalizedName, Revision = 1, CreatedAt = now, UpdatedAt = now };
                if (row is Organization org && copy is Organization target) { target.WebsiteUrl = org.WebsiteUrl; target.Memo = org.Memo; }
                if (row is Category cat && copy is Category category) { category.SortOrder = cat.SortOrder; category.ParentCategoryId = cat.ParentCategoryId.HasValue ? map[cat.ParentCategoryId.Value] : null; }
                Add(copy, destination);
            }
            return map;
        }
        var works = await Copy<Work>(); var series = await Copy<Series>(); var characters = await Copy<Character>();
        await Copy<Tag>(); await Copy<Organization>(); await Copy<Category>();
        foreach (var row in await db.Set<WorkSeries>().AsNoTracking().Where(x => EF.Property<Guid>(x, "CollectionId") == source).ToArrayAsync(token)) Add(new WorkSeries { WorkId = works[row.WorkId], SeriesId = series[row.SeriesId] }, destination);
        foreach (var row in await db.Set<CharacterWork>().AsNoTracking().Where(x => EF.Property<Guid>(x, "CollectionId") == source).ToArrayAsync(token)) Add(new CharacterWork { CharacterId = characters[row.CharacterId], WorkId = works[row.WorkId] }, destination);
        var settings = await db.Set<ImageSettings>().AsNoTracking().SingleAsync(x => EF.Property<Guid>(x, "CollectionId") == source, token);
        Add(new ImageSettings { KeepOriginal = settings.KeepOriginal, DisplayMaxEdge = settings.DisplayMaxEdge, DisplayQuality = settings.DisplayQuality, ThumbMaxEdge = settings.ThumbMaxEdge, ThumbQuality = settings.ThumbQuality, CreatedAt = now, UpdatedAt = now }, destination);
    }
}
