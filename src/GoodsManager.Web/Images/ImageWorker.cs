using System.Text.Json;
using GoodsManager.Web.Auth;
using GoodsManager.Web.Storage;
using Microsoft.EntityFrameworkCore;

namespace GoodsManager.Web.Images;

public sealed class ImageWorker(IServiceScopeFactory scopes, IImageFiles files, IImageConverter converter, ImageLimits limits, ImageActivity activity, AuthWriteGate gate, TimeProvider clock, ILogger<ImageWorker> logger) : BackgroundService
{
    public static async Task InitializeAsync(AppDbContext db, TimeProvider clock)
    {
        if (!await db.Set<ImageSettings>().AnyAsync()) { var now = clock.GetUtcNow(); db.Add(new ImageSettings { CreatedAt = now, UpdatedAt = now }); }
        await db.Set<TemporaryUpload>().Where(x => x.State == "processing").ExecuteUpdateAsync(x => x.SetProperty(y => y.State, "pending"));
        await db.SaveChangesAsync();
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextGc = clock.GetUtcNow().AddHours(1);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (clock.GetUtcNow() >= nextGc) { await using var gc = scopes.CreateAsyncScope(); await gc.ServiceProvider.GetRequiredService<ImageGc>().RunAsync(stoppingToken); nextGc = clock.GetUtcNow().AddHours(1); }
                Guid? id;
                await using (var scope = scopes.CreateAsyncScope()) id = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<TemporaryUpload>().Where(x => x.State == "pending").OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(stoppingToken);
                if (id.HasValue) await ProcessAsync(id.Value, stoppingToken); else await Task.Delay(500, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) { logger.LogError("画像ワーカー失敗 type={Type}", error.GetType().Name); await Task.Delay(1000, stoppingToken); }
        }
    }
    public async Task ProcessAsync(Guid id, CancellationToken token)
    {
        TemporaryUpload upload;
        CancellationTokenSource? conversionCancellation = null;
        await gate.Semaphore.WaitAsync(token);
        try
        {
            await using var scope = scopes.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            upload = await db.Set<TemporaryUpload>().SingleAsync(x => x.Id == id, token);
            if (upload.State != "pending") return;
            if (upload.ExpiresAt <= clock.GetUtcNow()) { upload.State = "cancelled"; upload.ErrorCode = "UPLOAD_EXPIRED"; }
            else upload.State = "processing";
            upload.UpdatedAt = clock.GetUtcNow(); await db.SaveChangesAsync(token);
            if (upload.State != "processing") return;
            conversionCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            lock (activity.Converting) activity.Converting.Add(id, conversionCancellation);
        }
        finally { gate.Semaphore.Release(); }
        using var conversion = conversionCancellation!;
        var reserve = 2L * (4096 * 4096 + 1024 * 1024) * 4;
        var jobDirectory = ""; var completed = false; var reserved = false;
        try
        {
            jobDirectory = files.NewJobDirectory(); lock (activity.Jobs) activity.Jobs.Add(jobDirectory);
            Interlocked.Add(ref activity.ReservedBytes, reserve); reserved = true;
            if (activity.TemporaryBytes(files.Root) + Interlocked.Read(ref activity.ReservedBytes) > limits.TemporaryMaxBytes) throw new ImageProblem("TEMPORARY_STORAGE_LIMIT");
            var policy = JsonSerializer.Deserialize<ImagePolicy>(upload.PolicyJson) ?? throw new ImageProblem("IMAGE_POLICY_INVALID"); policy.Validate();
            if (policy.Hash != upload.PolicyHash) throw new ImageProblem("IMAGE_POLICY_INVALID");
            var input = files.Resolve(upload.StagingPath!);
            if (!File.Exists(input) || await files.HashAsync(input, token) != upload.SourceSha256) throw new ImageProblem("IMAGE_SOURCE_MISSING");
            var image = await converter.ConvertAsync(new(input, jobDirectory, policy, limits.UploadMaxBytes, limits.MaxPixels, limits.MaxEdge), conversion.Token);
            await gate.Semaphore.WaitAsync(token);
            try
            {
                await using var scope = scopes.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await using var transaction = await db.Database.BeginTransactionAsync(token);
                var current = await db.Set<TemporaryUpload>().SingleAsync(x => x.Id == id, token);
                if (current.State != "processing" || current.ExpiresAt <= clock.GetUtcNow()) return;
                var now = clock.GetUtcNow();
                var asset = await db.Set<ImageAsset>().SingleOrDefaultAsync(x => x.SourceSha256 == upload.SourceSha256 && x.PolicyHash == upload.PolicyHash, token);
                if (asset is null)
                {
                    async Task<Guid> Publish(string path, string media, int width, int height)
                    {
                        var hash = await files.HashAsync(path, token);
                        var stored = await db.Set<ImageFile>().SingleOrDefaultAsync(x => x.Sha256 == hash, token) ?? db.Set<ImageFile>().Local.SingleOrDefault(x => x.Sha256 == hash);
                        await files.PublishAsync(path, hash, token);
                        if (stored is null)
                        {
                            stored = new ImageFile { Id = Guid.NewGuid(), Sha256 = hash, RelativePath = ImageFiles.ObjectPath(hash), MediaType = media, Width = width, Height = height, ByteLength = new FileInfo(path).Length, CreatedAt = now, UpdatedAt = now }; db.Add(stored);
                        }
                        stored.State = "ready"; stored.DeleteAfter = null; return stored.Id;
                    }
                    var display = await Publish(Path.Combine(jobDirectory, "display.webp"), "image/webp", image.DisplayWidth, image.DisplayHeight);
                    var thumb = await Publish(Path.Combine(jobDirectory, "thumbnail.webp"), "image/webp", image.ThumbnailWidth, image.ThumbnailHeight);
                    var original = policy.KeepOriginal ? await Publish(input, image.MediaType, image.SourceWidth, image.SourceHeight) : (Guid?)null;
                    asset = new ImageAsset { Id = Guid.NewGuid(), SourceSha256 = upload.SourceSha256, PolicyHash = upload.PolicyHash, PolicyJson = upload.PolicyJson, KeepOriginal = policy.KeepOriginal, DisplayFileId = display, ThumbnailFileId = thumb, OriginalFileId = original, CreatedAt = now }; db.Add(asset);
                }
                else
                {
                    var ids = new[] { asset.DisplayFileId, asset.ThumbnailFileId, asset.OriginalFileId }.OfType<Guid>().ToArray();
                    foreach (var file in await db.Set<ImageFile>().Where(x => ids.Contains(x.Id)).ToArrayAsync(token)) { await files.VerifyAsync(file, token); file.State = "ready"; file.DeleteAfter = null; }
                }
                current.State = "ready"; current.ImageAssetId = asset.Id; current.ErrorCode = null; current.StagingPath = null; current.UpdatedAt = now;
                await db.SaveChangesAsync(token); await transaction.CommitAsync(token); completed = true;
            }
            finally { gate.Semaphore.Release(); }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) when (conversion.IsCancellationRequested) { /* G03が確定した取消状態を保持する。 */ }
        catch (Exception error)
        {
            var code = error is ImageProblem problem ? problem.Message : error is IOException or UnauthorizedAccessException ? "IMAGE_STORAGE_FAILED" : "IMAGE_CONVERSION_FAILED";
            logger.LogWarning("画像処理失敗 uploadId={UploadId} code={Code} type={Type}", id, code, error.GetType().Name);
            await gate.Semaphore.WaitAsync(token);
            try
            {
                await using var scope = scopes.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var current = await db.Set<TemporaryUpload>().SingleOrDefaultAsync(x => x.Id == id, token);
                if (current?.State == "processing" && (error is IOException or UnauthorizedAccessException)) await scope.ServiceProvider.GetRequiredService<ImageAlerts>().SetAsync("image-upload:" + id.ToString("D"), code, id, token);
                if (current?.State == "processing") { current.State = "failed"; current.ErrorCode = code; current.UpdatedAt = clock.GetUtcNow(); await db.SaveChangesAsync(token); }
            }
            finally { gate.Semaphore.Release(); }
        }
        finally
        {
            lock (activity.Converting) activity.Converting.Remove(id);
            lock (activity.Jobs) activity.Jobs.Remove(jobDirectory);
            if (reserved) Interlocked.Add(ref activity.ReservedBytes, -reserve);
            if (completed && upload.StagingPath is not null) try { files.Delete(upload.StagingPath); } catch (IOException) { }
            // 自分で作ったGUIDジョブ領域だけ。障害時に残ったものはGCが猶予後に処理する。
            if (Path.GetDirectoryName(jobDirectory) == Path.Combine(files.Root, "jobs")) try { Directory.Delete(jobDirectory, true); } catch (IOException) { }
        }
    }
}
