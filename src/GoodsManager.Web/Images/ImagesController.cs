using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoodsManager.Web.Auth;
using GoodsManager.Web.Masters;
using GoodsManager.Web.Products;
using GoodsManager.Web.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GoodsManager.Web.Images;

[Authorize]
[Route("api/v1")]
public sealed class ImagesController(AppDbContext db, ImageUploads uploads, ProductOperations products, ProductWrites writes, ImageLinks links, SignInManager<AppUser> signIn, TimeProvider clock) : ControllerBase
{
    [HttpPost("temporary-uploads")]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> Upload() { var status = await uploads.AcceptAsync(HttpContext); Response.Headers.RetryAfter = "2"; return Accepted(status); }
    [HttpGet("temporary-uploads/{id:guid}")]
    public async Task<IActionResult> Status(Guid id) { Response.Headers.RetryAfter = "2"; return Ok(ImageUploads.Status(await uploads.OwnedAsync(id, User, HttpContext.RequestAborted), clock.GetUtcNow(), db.CollectionId)); }
    [HttpDelete("temporary-uploads/{id:guid}")]
    public async Task<IActionResult> Cancel(Guid id) { await uploads.CancelAsync(HttpContext, id); return NoContent(); }
    [HttpGet("products/{id:guid}/images")]
    public async Task<IActionResult> Images(Guid id)
    {
        var token = HttpContext.RequestAborted;
        await db.Database.OpenConnectionAsync(token); await using var native = ((Microsoft.Data.Sqlite.SqliteConnection)db.Database.GetDbConnection()).BeginTransaction(deferred: true); await using var tx = await db.Database.UseTransactionAsync(native, token);
        var product = await products.FindAsync(id, token); var result = await View(product, token); Response.Headers.ETag = ProductOperations.Etag(product); await native.CommitAsync(token); return Ok(result);
    }
    [HttpPut("products/{id:guid}/images")]
    [RequestSizeLimit(ProductWrites.BodyLimit)]
    public Task<IActionResult> Replace(Guid id) => writes.ExecuteAsync(HttpContext, $"PUT:/api/v1/products/{id:D}/images", false, async (body, now, token) =>
    {
        var product = await products.FindAsync(id, token); ProductWrites.CheckVersion(product, Request.Headers.IfMatch.ToString());
        var input = ProductWrites.Parse(() =>
        {
            var x = new InputObject(body, "entries", "primaryRef");
            return (Entries: ImageEditEntry.Parse(x.Get("entries")), Primary: x.NullableText("primaryRef", 200, trim: false));
        });
        var userId = Guid.Parse(signIn.UserManager.GetUserId(User)!);
        if (await links.ReplaceProductAsync(id, userId, input.Entries, input.Primary, token)) await products.TouchAsync(product, now, token);
        return (product, new Dictionary<string, Guid>());
    }, View);
    private async Task<object> View(Product product, CancellationToken token) => new { entries = await ImageLinks.ProductRefsAsync(db, product.Id, token), deduplicatedCount = links.DeduplicatedCount, productRevision = product.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture), etag = ProductOperations.Etag(product) };
}

[Authorize]
[Route("api/v1/settings/images")]
public sealed class ImageSettingsController(AppDbContext db, ImageLimits limits, AuthWriteGate gate, SignInManager<AppUser> signIn, TimeProvider clock) : ControllerBase
{
    private static string Etag(ImageSettings settings) => $"\"settings:images:{settings.Revision}\"";
    private object View(ImageSettings settings) => new { settings.KeepOriginal, settings.DisplayMaxEdge, settings.DisplayQuality, settings.ThumbMaxEdge, settings.ThumbQuality, limits.UploadMaxBytes, limits.MaxPixels, limits.MaxEdge, conversionTimeoutSeconds = limits.ConversionTimeout.TotalSeconds, workerMemoryBytes = limits.WorkerMemoryBytes, temporaryMaxBytes = limits.TemporaryMaxBytes, temporaryMaxCount = limits.TemporaryMaxCount, uploadLifetimeHours = limits.UploadLifetime.TotalHours, revision = settings.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture), etag = Etag(settings) };
    [HttpGet]
    public async Task<IActionResult> Get() { var settings = await db.Set<ImageSettings>().AsNoTracking().SingleAsync(); Response.Headers.ETag = Etag(settings); return Ok(View(settings)); }
    [HttpPut]
    [RequestSizeLimit(65536)]
    public async Task<IActionResult> Put()
    {
        var token = HttpContext.RequestAborted;
        if (!Request.HasJsonContentType()) throw new MasterProblem(415, "UNSUPPORTED_MEDIA_TYPE");
        var bytes = new byte[65537]; var length = 0; while (length < bytes.Length) { var count = await Request.Body.ReadAsync(bytes.AsMemory(length), token); if (count == 0) break; length += count; }
        if (length > 65536) throw new MasterProblem(413, "REQUEST_TOO_LARGE");
        if (!Guid.TryParseExact(Request.Headers["Idempotency-Key"].ToString(), "D", out var key) || key == Guid.Empty) throw InputObject.Invalid("idempotencyKey");
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Convert.ToHexStringLower(SHA256.HashData(bytes.AsSpan(0, length))) + "\n" + Request.Headers.IfMatch.ToString())));
        await gate.Semaphore.WaitAsync(token);
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(token);
            var user = await AuthenticationState.GetCurrentUserAsync(db, signIn, User) ?? throw new MasterProblem(401, "SESSION_EXPIRED"); var now = clock.GetUtcNow();
            const string scope = "PUT:/api/v1/settings/images";
            var saved = await db.Set<IdempotencyRecord>().SingleOrDefaultAsync(x => x.UserId == user.Id && x.OperationScope == scope && x.Key == key, token);
            if (saved is not null && saved.ExpiresAt > now) { if (saved.RequestHash != hash) throw new MasterProblem(409, "IDEMPOTENCY_KEY_REUSED"); Response.Headers.ETag = saved.ResponseEtag; return new ContentResult { Content = saved.ResponseJson, ContentType = "application/json", StatusCode = saved.ResponseStatus }; }
            if (saved is not null) { db.Remove(saved); await db.SaveChangesAsync(token); }
            var settings = await db.Set<ImageSettings>().SingleAsync(token);
            if (Request.Headers.IfMatch.ToString().Length == 0) throw new MasterProblem(428, "PRECONDITION_REQUIRED");
            if (Request.Headers.IfMatch.ToString() != Etag(settings)) throw new MasterProblem(412, "REVISION_MISMATCH");
            var policy = ProductWrites.Parse(() =>
            {
                using var json = JsonDocument.Parse(bytes.AsMemory(0, length)); var x = new InputObject(json.RootElement, "keepOriginal", "displayMaxEdge", "displayQuality", "thumbMaxEdge", "thumbQuality");
                if (x.Get("keepOriginal").ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw InputObject.Invalid("keepOriginal");
                return new ImagePolicy(x.Get("keepOriginal").GetBoolean(), x.Integer("displayMaxEdge"), x.Integer("displayQuality"), x.Integer("thumbMaxEdge"), x.Integer("thumbQuality"), ImagePolicy.Version);
            }); policy.Validate();
            if (policy != ImagePolicy.From(settings))
            {
                settings.KeepOriginal = policy.KeepOriginal; settings.DisplayMaxEdge = policy.DisplayMaxEdge; settings.DisplayQuality = policy.DisplayQuality; settings.ThumbMaxEdge = policy.ThumbMaxEdge; settings.ThumbQuality = policy.ThumbQuality; settings.Revision = checked(settings.Revision + 1); settings.UpdatedAt = now;
                var state = await db.SystemStates.SingleAsync(token); state.DataRevision = checked(state.DataRevision + 1); state.UpdatedAt = now;
            }
            var body = JsonSerializer.Serialize(View(settings), new JsonSerializerOptions(JsonSerializerDefaults.Web)); var etag = Etag(settings);
            db.Add(new IdempotencyRecord { UserId = user.Id, OperationScope = scope, Key = key, RequestHash = hash, ResponseStatus = 200, ResponseJson = body, ResponseEtag = etag, CompletedAt = now, ExpiresAt = now.AddDays(7) });
            await db.SaveChangesAsync(token); await transaction.CommitAsync(token); Response.Headers.ETag = etag; return Content(body, "application/json");
        }
        finally { gate.Semaphore.Release(); }
    }
}
