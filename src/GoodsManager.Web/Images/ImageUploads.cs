using System.Security.Cryptography;
using System.Text.Json;
using GoodsManager.Web.Auth;
using GoodsManager.Web.Storage;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace GoodsManager.Web.Images;

public sealed class ImageActivity
{
    public SemaphoreSlim ReceiveGate { get; } = new(1, 1);
    public HashSet<string> Receiving { get; } = [];
    public HashSet<string> Jobs { get; } = [];
    public Dictionary<Guid, CancellationTokenSource> Converting { get; } = [];
    public long ReservedBytes;
    public long TemporaryBytes(string root)
    {
        long bytes = 0;
        foreach (var name in new[] { "staging", "jobs" })
        {
            var directory = Path.Combine(root, name); if (!Directory.Exists(directory)) continue;
            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
                try { bytes = checked(bytes + new FileInfo(file).Length); } catch (FileNotFoundException) { }
        }
        return bytes;
    }
}
public sealed class ImageUploads(AppDbContext db, SignInManager<AppUser> signIn, AuthWriteGate gate, IImageFiles files, ImageLimits limits, ImageActivity activity, TimeProvider clock)
{
    public static object Status(TemporaryUpload upload, DateTimeOffset now, Guid? collectionId = null)
    {
        var expired = upload.State is not ("consumed" or "cancelled") && upload.ExpiresAt <= now;
        var state = expired ? "cancelled" : upload.State;
        return new
        {
            id = upload.Id,
            state,
            upload.ExpiresAt,
            previewUrl = state == "ready" ? $"/api/v1/temporary-uploads/{upload.Id:D}/preview?collectionId={collectionId:D}" : null,
            errorCode = expired ? "UPLOAD_EXPIRED" : upload.ErrorCode,
            progressStage = state switch { "pending" => "queued", "processing" => "converting", "ready" => "complete", _ => state },
            retryAfterSeconds = state is "pending" or "processing" ? 2 : (int?)null
        };
    }
    public async Task<TemporaryUpload> OwnedAsync(Guid id, System.Security.Claims.ClaimsPrincipal principal, CancellationToken token)
    {
        var userId = Guid.Parse(signIn.UserManager.GetUserId(principal)!);
        return await db.Set<TemporaryUpload>().SingleOrDefaultAsync(x => x.Id == id && x.UserId == userId, token) ?? throw new Masters.MasterProblem(404, "NOT_FOUND");
    }
    public async Task<object> AcceptAsync(HttpContext context)
    {
        var token = context.RequestAborted;
        if (!Guid.TryParseExact(context.Request.Headers["Client-Upload-Key"].ToString(), "D", out var key) || key == Guid.Empty) throw Masters.MasterInput.Invalid("clientUploadKey");
        if (!MediaTypeHeaderValue.TryParse(context.Request.ContentType, out var type) || !type.MediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase)) throw new Masters.MasterProblem(415, "UNSUPPORTED_MEDIA_TYPE");
        var boundary = HeaderUtilities.RemoveQuotes(type.Boundary).Value;
        if (string.IsNullOrWhiteSpace(boundary) || boundary.Length > 128) throw Masters.MasterInput.Invalid("file");
        var sizeFeature = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
        if (sizeFeature is { IsReadOnly: false }) sizeFeature.MaxRequestBodySize = checked(limits.UploadMaxBytes + 65536);
        await activity.ReceiveGate.WaitAsync(token);
        string? staging = null;
        try
        {
            var userId = Guid.Parse(signIn.UserManager.GetUserId(context.User)!); var now = StorageValues.FromMilliseconds(clock.GetUtcNow().ToUnixTimeMilliseconds());
            var prior = await db.Set<TemporaryUpload>().AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId && x.ClientUploadKey == key && x.ReplayUntil > now, token);
            if (prior is null)
            {
                // 一時領域の資源制限はアプリ全体で共有する。所有者の照会・再送は上の所属フィルターを維持する。
                if (await db.Set<TemporaryUpload>().IgnoreQueryFilters().CountAsync(x => x.State != "consumed" && x.State != "cancelled" && x.ExpiresAt > now, token) >= limits.TemporaryMaxCount) throw new Masters.MasterProblem(429, "UPLOAD_COUNT_LIMIT");
                if (activity.TemporaryBytes(files.Root) + Interlocked.Read(ref activity.ReservedBytes) + limits.UploadMaxBytes > limits.TemporaryMaxBytes) throw new Masters.MasterProblem(507, "TEMPORARY_STORAGE_LIMIT");
                staging = files.NewStagingPath(); lock (activity.Receiving) activity.Receiving.Add(staging);
            }
            var reader = new MultipartReader(boundary, context.Request.Body) { HeadersCountLimit = 16, HeadersLengthLimit = 8192 };
            var section = await reader.ReadNextSectionAsync(token) ?? throw Masters.MasterInput.Invalid("file");
            if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition) || disposition.DispositionType != "form-data" || HeaderUtilities.RemoveQuotes(disposition.Name).Value != "file" || (!disposition.FileName.HasValue && !disposition.FileNameStar.HasValue)) throw Masters.MasterInput.Invalid("file");
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using var destination = staging is null ? Stream.Null : new FileStream(files.Resolve(staging), FileMode.CreateNew, FileAccess.Write, FileShare.None);
            var buffer = new byte[65536]; long length = 0;
            while (true)
            {
                var count = await section.Body.ReadAsync(buffer, token); if (count == 0) break;
                length = checked(length + count); if (length > limits.UploadMaxBytes) throw new Masters.MasterProblem(413, "IMAGE_SIZE_LIMIT");
                hash.AppendData(buffer, 0, count); await destination.WriteAsync(buffer.AsMemory(0, count), token);
            }
            await destination.FlushAsync(token);
            if (destination is FileStream stream) stream.Flush(true);
            await destination.DisposeAsync();
            if (length < 1 || await reader.ReadNextSectionAsync(token) is not null) throw Masters.MasterInput.Invalid("file");
            var source = Convert.ToHexStringLower(hash.GetHashAndReset());
            await gate.Semaphore.WaitAsync(token);
            try
            {
                await using var transaction = await db.Database.BeginTransactionAsync(token);
                var user = await AuthenticationState.GetCurrentUserAsync(db, signIn, context.User);
                if (user is null) throw new Masters.MasterProblem(401, "SESSION_EXPIRED");
                now = StorageValues.FromMilliseconds(clock.GetUtcNow().ToUnixTimeMilliseconds());
                var old = await db.Set<TemporaryUpload>().SingleOrDefaultAsync(x => x.UserId == user.Id && x.ClientUploadKey == key, token);
                if (old is not null && old.ReplayUntil > now)
                {
                    if (old.SourceSha256 != source || old.ByteLength != length) throw new Masters.MasterProblem(409, "CLIENT_UPLOAD_KEY_REUSED");
                    return Status(old, now, db.CollectionId);
                }
                if (old is not null) { db.Remove(old); await db.SaveChangesAsync(token); }
                if (staging is null) throw new Masters.MasterProblem(409, "UPLOAD_REPLAY_EXPIRED");
                var settings = await db.Set<ImageSettings>().SingleAsync(token); var policy = ImagePolicy.From(settings);
                var upload = new TemporaryUpload { Id = Guid.NewGuid(), UserId = user.Id, ClientUploadKey = key, StagingPath = staging, SourceSha256 = source, ByteLength = length, PolicyHash = policy.Hash, PolicyJson = policy.Json, CreatedAt = now, UpdatedAt = now, ExpiresAt = now.Add(limits.UploadLifetime), ReplayUntil = now.Add(limits.ReplayLifetime) };
                db.Add(upload); await db.SaveChangesAsync(token); await transaction.CommitAsync(token);
                lock (activity.Receiving) activity.Receiving.Remove(staging); staging = null;
                return Status(upload, now, db.CollectionId);
            }
            finally { gate.Semaphore.Release(); }
        }
        catch (InvalidDataException) { throw new Masters.MasterProblem(400, "INVALID_MULTIPART"); }
        finally
        {
            try { if (staging is not null) { lock (activity.Receiving) activity.Receiving.Remove(staging); files.Delete(staging); } }
            finally { activity.ReceiveGate.Release(); }
        }
    }
    public async Task CancelAsync(HttpContext context, Guid id)
    {
        var token = context.RequestAborted; await gate.Semaphore.WaitAsync(token);
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(token);
            if (await AuthenticationState.GetCurrentUserAsync(db, signIn, context.User) is null) throw new Masters.MasterProblem(401, "SESSION_EXPIRED");
            var upload = await OwnedAsync(id, context.User, token);
            if (upload.State == "consumed") throw new Masters.MasterProblem(409, "UPLOAD_CONSUMED");
            var staging = upload.StagingPath; upload.StagingPath = null;
            await new ImageAlerts(db, clock).ResolveAsync("image-upload:" + id.ToString("D"), token);
            if (upload.State != "cancelled") { upload.State = "cancelled"; upload.ImageAssetId = null; upload.UpdatedAt = clock.GetUtcNow(); }
            await db.SaveChangesAsync(token); await transaction.CommitAsync(token);
            lock (activity.Converting) { if (activity.Converting.TryGetValue(id, out var conversion)) conversion.Cancel(); }
            if (staging is not null) try { files.Delete(staging); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { /* 残った一時ファイルはGCで再試行する。 */ }
        }
        finally { gate.Semaphore.Release(); }
    }
}
