using System.Text.Json;
using GoodsManager.Web.Auth;
using GoodsManager.Web.Storage;
using Microsoft.EntityFrameworkCore;

namespace GoodsManager.Web.Images;

public sealed class ImageAlerts(AppDbContext db, TimeProvider clock)
{
    public async Task ResolveAsync(string key, CancellationToken token) => await db.Set<SystemAlert>().Where(x => x.AlertKey == key && x.ResolvedAt == null).ExecuteUpdateAsync(x => x.SetProperty(y => y.ResolvedAt, clock.GetUtcNow()), token);
    public async Task SetAsync(string key, string code, Guid id, CancellationToken token)
    {
        var now = clock.GetUtcNow(); var alert = await db.Set<SystemAlert>().SingleOrDefaultAsync(x => x.AlertKey == key, token);
        if (alert is null) { alert = new SystemAlert { Id = Guid.NewGuid(), AlertKey = key, Code = code, FirstSeenAt = now }; db.Add(alert); }
        alert.LastSeenAt = now; alert.ResolvedAt = null; alert.SafeDetailsJson = JsonSerializer.Serialize(new { id }); await db.SaveChangesAsync(token);
    }
}
public sealed class ImageGc(AppDbContext db, IImageFiles files, ImageActivity activity, ImageLimits limits, TimeProvider clock, AuthWriteGate gate, ILogger<ImageGc> logger)
{
    public async Task RunAsync(CancellationToken token)
    {
        await gate.Semaphore.WaitAsync(token);
        try
        {
            var now = clock.GetUtcNow(); var removeStaging = new List<string>(); var cancelConversions = new List<Guid>(); var orphanFailed = false;
            void Reclaim(Action action) { try { action(); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { orphanFailed = true; } }
            foreach (var upload in await db.Set<TemporaryUpload>().Where(x => x.State != "consumed" && x.ExpiresAt <= now).ToArrayAsync(token))
            {
                if (upload.StagingPath is not null) removeStaging.Add(upload.StagingPath);
                cancelConversions.Add(upload.Id); upload.State = "cancelled"; upload.ErrorCode ??= "UPLOAD_EXPIRED";
                await new ImageAlerts(db, clock).ResolveAsync("image-upload:" + upload.Id.ToString("D"), token); upload.StagingPath = null; upload.ImageAssetId = null; upload.UpdatedAt = now;
            }
            foreach (var upload in await db.Set<TemporaryUpload>().Where(x => x.State == "consumed" || x.State == "cancelled").ToArrayAsync(token))
            {
                if (upload.StagingPath is not null) removeStaging.Add(upload.StagingPath);
                upload.ImageAssetId = null; upload.StagingPath = null;
            }
            await db.SaveChangesAsync(token);
            lock (activity.Converting) foreach (var id in cancelConversions) if (activity.Converting.TryGetValue(id, out var conversion)) conversion.Cancel();
            await db.Set<TemporaryUpload>().Where(x => x.ReplayUntil <= now && x.State != "pending" && x.State != "processing" && x.State != "ready").ExecuteDeleteAsync(token);
            var alerts = new ImageAlerts(db, clock);
            var formalAssets = db.Set<ImageAsset>().Where(a => db.Set<ProductImage>().Any(p => p.ImageAssetId == a.Id) || db.Set<InventoryItemImage>().Any(i => i.ImageAssetId == a.Id));
            var formalFiles = await db.Set<ImageFile>().Where(f => formalAssets.Any(a => a.DisplayFileId == f.Id || a.ThumbnailFileId == f.Id || a.OriginalFileId == f.Id)).ToArrayAsync(token);
            foreach (var file in formalFiles)
            {
                try { await files.VerifyAsync(file, token); await alerts.ResolveAsync("image-file:" + file.Id.ToString("D"), token); }
                catch (IOException) { await alerts.SetAsync("image-file:" + file.Id.ToString("D"), "IMAGE_FILE_UNAVAILABLE", file.Id, token); }
            }
            var unusedAssets = await db.Set<ImageAsset>().Where(a => !db.Set<ProductImage>().Any(p => p.ImageAssetId == a.Id) && !db.Set<InventoryItemImage>().Any(i => i.ImageAssetId == a.Id) && !db.Set<TemporaryUpload>().Any(u => u.ImageAssetId == a.Id)).ToArrayAsync(token);
            db.RemoveRange(unusedAssets); await db.SaveChangesAsync(token);
            var unusedFiles = await db.Set<ImageFile>().Where(f => !db.Set<ImageAsset>().Any(a => a.DisplayFileId == f.Id || a.ThumbnailFileId == f.Id || a.OriginalFileId == f.Id)).ToArrayAsync(token);
            foreach (var file in unusedFiles)
            {
                await alerts.ResolveAsync("image-file:" + file.Id.ToString("D"), token);
                if (file.State == "ready") { file.State = "deletePending"; file.DeleteAfter = now.Add(limits.GcGrace); file.UpdatedAt = now; }
                else if (file.DeleteAfter <= now)
                {
                    try { files.Delete(file.RelativePath); db.Remove(file); await alerts.ResolveAsync("image-gc:" + file.Id.ToString("D"), token); }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException) { await alerts.SetAsync("image-gc:" + file.Id.ToString("D"), "IMAGE_GC_FAILED", file.Id, token); logger.LogWarning("画像GC削除失敗 fileId={Id} type={Type}", file.Id, error.GetType().Name); }
                }
            }
            await db.SaveChangesAsync(token);
            foreach (var path in removeStaging.Distinct()) Reclaim(() => files.Delete(path));
            // 公開後DB確定前の停止で残ったファイル。一致する管理パスだけを猶予後に回収。
            var objects = Path.Combine(files.Root, "objects");
            if (Directory.Exists(objects)) foreach (var path in Directory.EnumerateFiles(objects, "*", SearchOption.AllDirectories))
            {
                if (File.GetLastWriteTimeUtc(path) > now.Subtract(limits.GcGrace).UtcDateTime) continue;
                var relative = Path.GetRelativePath(files.Root, path).Replace('\\', '/');
                var name = Path.GetFileName(path);
                if (name.Length == 64 && name.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f') && relative == ImageFiles.ObjectPath(name) && !await db.Set<ImageFile>().AnyAsync(x => x.Sha256 == name, token))
                    Reclaim(() => files.Delete(relative));
            }
            // 同一ボリュームへの公開途中ファイルと、停止時に残った自前ジョブ領域。
            if (Directory.Exists(objects)) foreach (var path in Directory.EnumerateFiles(objects, "*.partial", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }))
            {
                var parts = Path.GetFileName(path).Split('.');
                if (parts.Length != 3 || parts[0].Length != 64 || !parts[0].All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f') || !Guid.TryParseExact(parts[1], "N", out _) || parts[2] != "partial") continue;
                var destination = files.Resolve(ImageFiles.ObjectPath(parts[0]));
                if (Path.GetDirectoryName(path) == Path.GetDirectoryName(destination) && File.GetLastWriteTimeUtc(path) <= now.Subtract(limits.GcGrace).UtcDateTime) Reclaim(() => File.Delete(path));
            }
            var jobs = Path.Combine(files.Root, "jobs");
            if (Directory.Exists(jobs)) foreach (var directory in Directory.EnumerateDirectories(jobs))
            {
                var name = Path.GetFileName(directory);
                if (!Guid.TryParseExact(name, "N", out _) || name != name.ToLowerInvariant()) continue;
                lock (activity.Jobs) { if (activity.Jobs.Contains(directory)) continue; }
                if (Directory.GetLastWriteTimeUtc(directory) > now.Subtract(limits.GcGrace).UtcDateTime) continue;
                // Resolveは親ディレクトリを含むreparse pointと領域外パスを拒否する。
                if (Path.GetDirectoryName(files.Resolve($"jobs/{name}/job.json")) != directory) continue;
                Reclaim(() =>
                {
                    foreach (var path in Directory.EnumerateFiles(directory)) files.Delete($"jobs/{name}/{Path.GetFileName(path)}");
                    if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
                });
            }
            var staging = Path.Combine(files.Root, "staging");
            var live = (await db.Set<TemporaryUpload>().Where(x => x.StagingPath != null).Select(x => x.StagingPath!).ToArrayAsync(token)).ToHashSet(StringComparer.Ordinal);
            lock (activity.Receiving) live.UnionWith(activity.Receiving);
            if (Directory.Exists(staging)) foreach (var path in Directory.EnumerateFiles(staging, "*.input"))
            {
                var relative = "staging/" + Path.GetFileName(path);
                if (!live.Contains(relative) && File.GetLastWriteTimeUtc(path) <= now.Subtract(limits.GcGrace).UtcDateTime) Reclaim(() => files.Delete(relative));
            }
            if (orphanFailed) { await alerts.SetAsync("image-gc:orphan", "IMAGE_GC_FAILED", Guid.Empty, token); logger.LogWarning("画像GCで一時・孤立ファイルの削除に失敗。次回再試行します。"); }
            else await alerts.ResolveAsync("image-gc:orphan", token);
        }
        finally { gate.Semaphore.Release(); }
    }
}
