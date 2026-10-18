using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoodsManager.Web.Auth;
using GoodsManager.Web.Masters;
using GoodsManager.Web.Storage;
using GoodsManager.Web.Search;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GoodsManager.Web.Products;

/// <summary>変更と成功応答記録を一括確定し、再送は古いETagの検査より先に照会する。</summary>
public sealed class ProductWrites(AppDbContext db, SignInManager<AppUser> signIn, AuthWriteGate gate, TimeProvider clock, ProductOperations products, SearchProjection projection)
{
    public const int BodyLimit = 256 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IActionResult> ExecuteAsync(HttpContext context, string scope, bool create,
        Func<JsonElement, DateTimeOffset, CancellationToken, Task<(Product Product, IReadOnlyDictionary<string, Guid> Refs)>> action)
    {
        var request = context.Request;
        var token = context.RequestAborted;
        if (!request.HasJsonContentType()) throw new MasterProblem(415, "UNSUPPORTED_MEDIA_TYPE");
        var bytes = new byte[BodyLimit + 1];
        var length = 0;
        while (length < bytes.Length)
        {
            var count = await request.Body.ReadAsync(bytes.AsMemory(length), token);
            if (count == 0) break;
            length += count;
        }
        if (length > BodyLimit) throw new MasterProblem(413, "REQUEST_TOO_LARGE");
        if (!Guid.TryParseExact(request.Headers["Idempotency-Key"].ToString(), "D", out var key) || key == Guid.Empty) throw InputObject.Invalid("idempotencyKey");
        var bodyHash = Convert.ToHexStringLower(SHA256.HashData(bytes.AsSpan(0, length)));
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[] { bodyHash, request.Headers.IfMatch.ToString() }))));
        await gate.Semaphore.WaitAsync(token);
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(token);
            var user = await AuthenticationState.GetCurrentUserAsync(db, signIn, context.User);
            if (user is null) throw new MasterProblem(401, "SESSION_EXPIRED");
            var now = StorageValues.FromMilliseconds(clock.GetUtcNow().ToUnixTimeMilliseconds());
            var previous = await db.Set<IdempotencyRecord>().SingleOrDefaultAsync(x => x.UserId == user.Id && x.OperationScope == scope && x.Key == key, token);
            if (previous is not null && previous.ExpiresAt > now)
            {
                if (previous.RequestHash != hash) throw new MasterProblem(409, "IDEMPOTENCY_KEY_REUSED");
                return Stored(context, previous);
            }
            if (previous is not null) db.Entry(previous).State = EntityState.Detached;
            await db.Set<IdempotencyRecord>().Where(x => x.ExpiresAt <= now).ExecuteDeleteAsync(token);
            JsonDocument document;
            try { document = JsonDocument.Parse(bytes.AsMemory(0, length)); }
            catch (JsonException) { throw InputObject.Invalid("body"); }
            using var parsed = document;
            var result = await action(document.RootElement, now, token);
            await db.SaveChangesAsync(token);
            await projection.RebuildAsync([result.Product.Id], token);
            var body = create ? await products.DetailAsync(result.Product, token) : await products.InventoryAsync(result.Product, result.Refs, token);
            var completedAt = StorageValues.FromMilliseconds(clock.GetUtcNow().ToUnixTimeMilliseconds());
            var saved = new IdempotencyRecord
            {
                UserId = user.Id,
                OperationScope = scope,
                Key = key,
                RequestHash = hash,
                ResponseStatus = create ? 201 : 200,
                ResponseJson = JsonSerializer.Serialize(body, Json),
                ResponseEtag = ProductOperations.Etag(result.Product),
                Location = create ? $"/api/v1/products/{result.Product.Id:D}" : null,
                CompletedAt = completedAt,
                ExpiresAt = completedAt.AddDays(7)
            };
            db.Add(saved);
            await db.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
            return Stored(context, saved);
        }
        finally { gate.Semaphore.Release(); }
    }
    private static IActionResult Stored(HttpContext context, IdempotencyRecord saved)
    {
        context.Response.Headers.ETag = saved.ResponseEtag;
        if (saved.Location is not null) context.Response.Headers.Location = saved.Location;
        return new ContentResult { StatusCode = saved.ResponseStatus, ContentType = "application/json; charset=utf-8", Content = saved.ResponseJson };
    }
    public static void CheckVersion(Product product, string condition)
    {
        if (condition.Length == 0) throw new MasterProblem(428, "PRECONDITION_REQUIRED");
        if (condition != ProductOperations.Etag(product)) throw new MasterProblem(412, "REVISION_MISMATCH");
    }
    public static T Parse<T>(Func<T> parse)
    {
        try { return parse(); }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or ArgumentException or OverflowException) { throw InputObject.Invalid("body"); }
    }
}
