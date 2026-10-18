using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoodsManager.Web.Auth;
using GoodsManager.Web.Images;
using GoodsManager.Web.Masters;
using GoodsManager.Web.Products;
using GoodsManager.Web.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GoodsManager.Web.Backups;

/// <summary>通常認証・CSRF・固定した再送条件で設定、受付、履歴を扱う</summary>
[Authorize]
[Route("api/v1")]
public sealed class BackupsController(AppDbContext db, BackupService service, BackupOptions options, BackupGate maintenance, AuthWriteGate writes, BackupSignal signal, SignInManager<AppUser> signIn, TimeProvider clock) : ControllerBase
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static string Etag(BackupSettings x) => $"\"settings:backups:{x.Revision}\"";
    private async Task<object> SettingsViewAsync(BackupSettings x, CancellationToken token) => new
    {
        x.Enabled,
        x.LocalTime,
        x.TimeZone,
        automaticRetention = 3,
        destinationDisplay = options.DirectoryPath,
        revision = x.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture),
        etag = Etag(x),
        lastSuccess = await db.Set<BackupRun>().AsNoTracking().Where(r => r.State == "succeeded").OrderByDescending(r => r.CompletedAt).Select(r => r.CompletedAt).FirstOrDefaultAsync(token)
    };
    public static object RunView(BackupRun x) => new
    {
        id = x.Id,
        x.Kind,
        x.State,
        x.StartedAt,
        x.CompletedAt,
        snapshotDataRevision = x.SnapshotDataRevision?.ToString(System.Globalization.CultureInfo.InvariantCulture),
        x.ByteLength,
        x.ErrorCode,
        downloadable = x.State == "succeeded",
        x.RetentionManaged
    };

    /// <summary>変更可能項目と環境設定の参照表示を返す</summary>
    [HttpGet("settings/backups")]
    public async Task<IActionResult> Settings(CancellationToken token)
    {
        var settings = await db.Set<BackupSettings>().AsNoTracking().SingleAsync(token); Response.Headers.ETag = Etag(settings); return Ok(await SettingsViewAsync(settings, token));
    }
    /// <summary>最新番号と一致する設定だけを保存</summary>
    [HttpPut("settings/backups")]
    [RequestSizeLimit(65536)]
    public Task<IActionResult> SaveSettings() => WriteAsync("PUT:/settings/backups", async (body, token) =>
    {
        var settings = await db.Set<BackupSettings>().SingleAsync(token);
        if (Request.Headers.IfMatch.ToString().Length == 0) throw new MasterProblem(428, "PRECONDITION_REQUIRED");
        if (Request.Headers.IfMatch.ToString() != Etag(settings)) throw new MasterProblem(412, "REVISION_MISMATCH");
        var input = new InputObject(body, "enabled", "localTime");
        if (input.Get("enabled").ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw InputObject.Invalid("enabled");
        var enabled = input.Get("enabled").GetBoolean(); var time = input.Get("localTime").GetString();
        if (time is null || !BackupPaths.ValidTime(time)) throw InputObject.Invalid("localTime");
        if (settings.Enabled != enabled || settings.LocalTime != time)
        {
            settings.Enabled = enabled; settings.LocalTime = time; settings.Revision = checked(settings.Revision + 1); settings.UpdatedAt = clock.GetUtcNow();
            var state = await db.SystemStates.SingleAsync(token); state.DataRevision = checked(state.DataRevision + 1); state.UpdatedAt = clock.GetUtcNow();
        }
        return (200, await SettingsViewAsync(settings, token), Etag(settings), (string?)null);
    });

    /// <summary>実行IDの受付を再送記録と同時に確定</summary>
    [HttpPost("backups")]
    [RequestSizeLimit(65536)]
    public async Task<IActionResult> Create()
    {
        var result = await WriteAsync("POST:/backups", async (body, token) =>
        {
            var input = new InputObject(body, "kind");
            if (input.Get("kind").ValueKind != JsonValueKind.String || input.Get("kind").GetString() != "manual") throw InputObject.Invalid("kind");
            if (await db.Set<BackupRun>().AnyAsync(x => x.State == "running", token)) throw new MasterProblem(409, "BACKUP_IN_PROGRESS");
            var run = new BackupRun { Id = Guid.NewGuid(), StartedAt = clock.GetUtcNow(), RetentionManaged = true };
            db.Add(run); return (202, RunView(run), "", $"/api/v1/backups/{run.Id:D}");
        });
        signal.Wake(); return result;
    }

    /// <summary>入力件数を制限し、履歴のページを取得</summary>
    [HttpGet("backups")]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken token = default)
    {
        if (page < 1 || page > int.MaxValue / 100 || pageSize is not (25 or 50 or 100)) throw InputObject.Invalid("page");
        var total = await db.Set<BackupRun>().CountAsync(token); var pages = Math.Max(1, (total + pageSize - 1) / pageSize); page = Math.Min(page, pages);
        var rows = await db.Set<BackupRun>().AsNoTracking().OrderByDescending(x => x.StartedAt).ThenByDescending(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(token);
        return Ok(new { items = rows.Select(RunView), total, page, pageSize, totalPages = pages });
    }
    /// <summary>受付結果と実処理の成否を区別して返す</summary>
    [HttpGet("backups/{id:guid}")]
    public async Task<IActionResult> Detail(Guid id, CancellationToken token) => Ok(RunView(await db.Set<BackupRun>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, token) ?? throw new MasterProblem(404, "NOT_FOUND")));

    /// <summary>取得直前にも全ZIPを再検証し、確保した読取ストリームを返す</summary>
    [HttpGet("backups/{id:guid}/download")]
    public async Task<IActionResult> Download(Guid id, CancellationToken token)
    {
        await maintenance.Semaphore.WaitAsync(token);
        FileStream? input = null;
        try
        {
            var run = await db.Set<BackupRun>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, token) ?? throw new MasterProblem(404, "NOT_FOUND");
            if (run.State != "succeeded") throw new MasterProblem(409, "BACKUP_NOT_DOWNLOADABLE");
            input = new FileStream(service.Resolve(run), FileMode.Open, FileAccess.Read, FileShare.Read);
            var manifest = await service.VerifyStreamAsync(input, token);
            if (BackupService.ManifestHash(manifest) != run.ManifestHash || input.Length != run.ByteLength) throw new InvalidDataException("履歴とZIPが不一致");
            input.Position = 0; return File(input, "application/zip", $"goods-manager-{id:D}.zip", enableRangeProcessing: false);
        }
        catch (Exception error) when (error is not (OperationCanceledException or OutOfMemoryException or MasterProblem))
        {
            if (input is not null) await input.DisposeAsync();
            await writes.Semaphore.WaitAsync(token);
            try
            {
                var run = await db.Set<BackupRun>().SingleOrDefaultAsync(x => x.Id == id, token);
                if (run?.State == "succeeded") { run.State = "failed"; run.ErrorCode = "BACKUP_ARCHIVE_UNAVAILABLE"; await db.SaveChangesAsync(token); }
                await new ImageAlerts(db, clock).SetAsync("backup:archive:" + id.ToString("D"), "BACKUP_ARCHIVE_UNAVAILABLE", id, token);
            }
            finally { writes.Semaphore.Release(); }
            throw new MasterProblem(409, "BACKUP_ARCHIVE_UNAVAILABLE");
        }
        catch { if (input is not null) await input.DisposeAsync(); throw; }
        finally { maintenance.Semaphore.Release(); }
    }

    /// <summary>対象IDの明示確認後に論理削除を確定し、実体整理へ渡す</summary>
    [HttpDelete("backups/{id:guid}")]
    [RequestSizeLimit(65536)]
    public async Task<IActionResult> Delete(Guid id)
    {
        // 先に論理削除を確定し、ファイル削除失敗時はdeletingから安全に再開する。
        var result = await WriteAsync($"DELETE:/backups/{id:D}", async (body, token) =>
        {
            _ = new InputObject(body);
            if (Request.Headers["X-Confirm-Delete"].ToString() != id.ToString("D")) throw InputObject.Invalid("confirmation");
            var run = await db.Set<BackupRun>().SingleOrDefaultAsync(x => x.Id == id, token) ?? throw new MasterProblem(404, "NOT_FOUND");
            if (run.State == "running") throw new MasterProblem(409, "BACKUP_IN_PROGRESS");
            run.State = run.RelativePath is null ? "deleted" : "deleting"; return (204, (object?)null, "", (string?)null);
        });
        signal.Wake(); return result;
    }

    /// <summary>受付や論理削除も成功記録と同時に確定し、再送をETagの再検証より先に照会</summary>
    private async Task<IActionResult> WriteAsync(string scope, Func<JsonElement, CancellationToken, Task<(int Status, object? Body, string Etag, string? Location)>> action)
    {
        var token = HttpContext.RequestAborted;
        if (!Request.HasJsonContentType()) throw new MasterProblem(415, "UNSUPPORTED_MEDIA_TYPE");
        var bytes = new byte[65537]; var length = 0;
        while (length < bytes.Length) { var count = await Request.Body.ReadAsync(bytes.AsMemory(length), token); if (count == 0) break; length += count; }
        if (length > 65536) throw new MasterProblem(413, "REQUEST_TOO_LARGE");
        if (!Guid.TryParseExact(Request.Headers["Idempotency-Key"].ToString(), "D", out var key) || key == Guid.Empty) throw InputObject.Invalid("idempotencyKey");
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[] { Convert.ToHexStringLower(SHA256.HashData(bytes.AsSpan(0, length))), Request.Headers.IfMatch.ToString(), Request.Headers["X-Confirm-Delete"].ToString() }))));
        await writes.Semaphore.WaitAsync(token);
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(token);
            var user = await AuthenticationState.GetCurrentUserAsync(db, signIn, User) ?? throw new MasterProblem(401, "SESSION_EXPIRED");
            var now = clock.GetUtcNow();
            var saved = await db.Set<IdempotencyRecord>().SingleOrDefaultAsync(x => x.UserId == user.Id && x.OperationScope == scope && x.Key == key, token);
            if (saved is not null && saved.ExpiresAt > now) { if (saved.RequestHash != hash) throw new MasterProblem(409, "IDEMPOTENCY_KEY_REUSED"); return Stored(saved); }
            if (saved is not null) { db.Remove(saved); await db.SaveChangesAsync(token); }
            (int Status, object? Body, string Etag, string? Location) result;
            try
            {
                using var json = JsonDocument.Parse(bytes.AsMemory(0, length)); result = await action(json.RootElement, token);
            }
            catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException or OverflowException or ArgumentException) { throw InputObject.Invalid("body"); }
            var completedAt = clock.GetUtcNow();
            saved = new IdempotencyRecord { UserId = user.Id, OperationScope = scope, Key = key, RequestHash = hash, ResponseStatus = result.Status, ResponseJson = JsonSerializer.Serialize(result.Body, Json), ResponseEtag = result.Etag, Location = result.Location, CompletedAt = completedAt, ExpiresAt = completedAt.AddDays(7) };
            db.Add(saved); await db.SaveChangesAsync(token); await tx.CommitAsync(token); return Stored(saved);
        }
        finally { writes.Semaphore.Release(); }
    }
    private IActionResult Stored(IdempotencyRecord saved)
    {
        if (saved.ResponseEtag.Length > 0) Response.Headers.ETag = saved.ResponseEtag;
        if (saved.Location is not null) Response.Headers.Location = saved.Location;
        return saved.ResponseStatus == 204 ? NoContent() : new ContentResult { StatusCode = saved.ResponseStatus, Content = saved.ResponseJson, ContentType = "application/json" };
    }
}
