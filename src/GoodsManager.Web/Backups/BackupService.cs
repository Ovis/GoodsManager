using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using GoodsManager.Web.Auth;
using GoodsManager.Web.Images;
using GoodsManager.Web.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GoodsManager.Web.Backups;

/// <summary>受付と実処理を分け、成功確定後だけ自動世代を整理する</summary>
public sealed class BackupService(AppDbContext db, DatabaseRuntime runtime, BackupOptions options, BackupGate maintenance, AuthWriteGate writes, TimeProvider clock, ILogger<BackupService> logger)
{
    /// <summary>マニフェスト内容と保存済み履歴を照合するハッシュ</summary>
    public static string ManifestHash(BackupManifest manifest) => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(manifest, BackupArchive.Json)));
    public string WorkRoot => Path.Combine(runtime.DirectoryPath, ".backup-work");

    /// <summary>公開済みZIPと履歴を照合。外部配置を自動世代の削除対象にしない</summary>
    public async Task InitializeAsync(CancellationToken token)
    {
        BackupPaths.RejectLinks(options.DirectoryPath); Directory.CreateDirectory(options.DirectoryPath);
        await maintenance.Semaphore.WaitAsync(token);
        try
        {
            await writes.Semaphore.WaitAsync(token);
            try
            {
                var archiveFailure = false;
                foreach (var run in await db.Set<BackupRun>().Where(x => x.State == "running").ToArrayAsync(token))
                { run.State = "failed"; run.CompletedAt = clock.GetUtcNow(); run.ErrorCode = "BACKUP_INTERRUPTED"; }
                await db.SaveChangesAsync(token);
                // 中断履歴と一致する自前の未完成ZIPだけを回収し、外部ファイルを残す。
                foreach (var interrupted in await db.Set<BackupRun>().Where(x => x.ErrorCode == "BACKUP_INTERRUPTED").Select(x => x.Id).ToArrayAsync(token))
                {
                    var partial = Path.Combine(options.DirectoryPath, interrupted.ToString("N") + ".zip.partial");
                    BackupPaths.RejectLinks(partial);
                    try { File.Delete(partial); }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                    { logger.LogWarning("未完成ZIP整理待ち Id={Id} Code={Code}", interrupted, "BACKUP_CLEANUP_FAILED"); }
                }
                foreach (var run in await db.Set<BackupRun>().Where(x => x.State == "deleting").ToArrayAsync(token))
                    try { await DeleteFileAsync(run, token); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { logger.LogWarning("バックアップ削除の再開待ち Id={Id} Code={Code}", run.Id, "BACKUP_DELETE_FAILED"); }
                // 前回のプロセスが残した自前の準備領域だけを整理する。
                if (Directory.Exists(WorkRoot)) foreach (var path in Directory.EnumerateDirectories(WorkRoot))
                    if (Guid.TryParseExact(Path.GetFileName(path), "N", out _)) BackupPaths.DeleteTree(WorkRoot, path);
                var locations = new[] { (Root: options.DirectoryPath, Prefix: ""), (Root: Path.Combine(runtime.DirectoryPath, "pre-migration"), Prefix: "pre-migration/") };
                foreach (var location in locations)
                {
                    if (!Directory.Exists(location.Root)) continue;
                    BackupPaths.RejectLinks(location.Root);
                    foreach (var path in Directory.EnumerateFiles(location.Root, "*.zip"))
                    {
                        var relative = location.Prefix + Path.GetFileName(path);
                        var existing = await db.Set<BackupRun>().SingleOrDefaultAsync(x => x.RelativePath == relative, token);
                        if (existing?.State is "succeeded" or "deleting" or "deleted") continue;
                        try
                        {
                            BackupPaths.RejectLinks(path);
                            await using var input = File.OpenRead(path);
                            // P02の旧退避は対応版での復旧用としてそのまま保持し、新形式の履歴とは分ける。
                            if (location.Prefix.Length > 0)
                            {
                                BackupZipDirectory.Check(input, options, token);
                                using var zip = new System.IO.Compression.ZipArchive(input, System.IO.Compression.ZipArchiveMode.Read, leaveOpen: true);
                                if (zip.GetEntry("manifest.json") is null) continue;
                            }
                            var manifest = await VerifyStreamAsync(input, token);
                            existing ??= await db.Set<BackupRun>().SingleOrDefaultAsync(x => x.Id == manifest.RunId, token);
                            if (existing is not null && (existing.Id != manifest.RunId || existing.State is "succeeded" or "deleted" or "deleting")) continue;
                            var managed = IsOwned(path, manifest);
                            var run = existing ?? new BackupRun { Id = manifest.RunId };
                            run.Kind = managed || location.Prefix.Length > 0 ? manifest.Kind : "external";
                            run.State = "succeeded"; run.StartedAt = manifest.CreatedAt; run.CompletedAt = manifest.CreatedAt;
                            run.SnapshotDataRevision = manifest.DataRevision; run.RelativePath = relative; run.ManifestHash = ManifestHash(manifest); run.ByteLength = input.Length; run.RetentionManaged = managed; run.ErrorCode = null;
                            if (existing is null) db.Add(run);
                            await db.SaveChangesAsync(token);
                            await new ImageAlerts(db, clock).ResolveAsync("backup:archive:" + run.Id.ToString("D"), token);
                        }
                        catch (Exception error) when (error is not (OperationCanceledException or OutOfMemoryException))
                        { archiveFailure = true; logger.LogWarning("バックアップ取込不可 Code={Code} Type={Type}", "BACKUP_ARCHIVE_UNAVAILABLE", error.GetType().Name); }
                    }
                }
                foreach (var run in await db.Set<BackupRun>().Where(x => x.State == "succeeded" || x.ErrorCode == "BACKUP_ARCHIVE_MISSING" && x.State == "failed").ToArrayAsync(token))
                    if (!File.Exists(Resolve(run))) { archiveFailure = true; run.State = "failed"; run.ErrorCode = "BACKUP_ARCHIVE_MISSING"; }
                await db.SaveChangesAsync(token);
                if (archiveFailure) await AlertAsync("BACKUP_ARCHIVE_UNAVAILABLE", token);
                else await new ImageAlerts(db, clock).ResolveAsync("backup:archive", token);
                if (await db.Set<BackupRun>().AnyAsync(x => x.ErrorCode == "BACKUP_INTERRUPTED", token)) await AlertAsync("BACKUP_INTERRUPTED", token);
            }
            finally { writes.Semaphore.Release(); }
        }
        finally { maintenance.Semaphore.Release(); }
    }


    /// <summary>日付を受付と同時に確定し、再起動しても当日の失敗を無限再試行しない</summary>
    public async Task TickAsync(CancellationToken token)
    {
        await ProcessDeletionsAsync(token);
        await writes.Semaphore.WaitAsync(token);
        try
        {
            db.ChangeTracker.Clear();
            var settings = await db.Set<BackupSettings>().SingleAsync(token);
            var local = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo"));
            var date = local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (!await db.Users.AnyAsync(token)) return;
            await TrackKeysAsync(BackupSnapshot.ReadKeys(runtime.DirectoryPath, maintenance.KeyLock), token);
            if (local.DayOfWeek == DayOfWeek.Sunday && settings.LastIntegrityDate != date)
            {
                // 検査の成否によらず同日の繰り返しを防ぎ、失敗は警告として残す。
                settings.LastIntegrityDate = date; await db.SaveChangesAsync(token);
                await db.Database.OpenConnectionAsync(token);
                try { await DatabaseMaintenance.VerifyAsync((SqliteConnection)db.Database.GetDbConnection()); await new ImageAlerts(db, clock).ResolveAsync("backup:integrity", token); }
                catch (Exception error) when (error is SqliteException or InvalidOperationException)
                { await new ImageAlerts(db, clock).SetAsync("backup:integrity", "BACKUP_INTEGRITY_FAILED", Guid.Empty, token); }
            }
            if (settings.Enabled && string.CompareOrdinal(local.ToString("HH:mm", CultureInfo.InvariantCulture), settings.LocalTime) >= 0 && settings.LastAutomaticDate != date && !await db.Set<BackupRun>().AnyAsync(x => x.State == "running", token))
            {
                settings.LastAutomaticDate = date;
                var revision = (await db.SystemStates.SingleAsync(token)).DataRevision;
                var previous = await db.Set<BackupRun>().Where(x => x.State == "succeeded").OrderByDescending(x => x.CompletedAt).FirstOrDefaultAsync(token);
                if (previous is null || previous.SnapshotDataRevision != revision) db.Add(new BackupRun { Id = Guid.NewGuid(), Kind = "automatic", StartedAt = clock.GetUtcNow(), RetentionManaged = true });
                await db.SaveChangesAsync(token);
            }
        }
        finally { writes.Semaphore.Release(); }
        var pending = await db.Set<BackupRun>().AsNoTracking().Where(x => x.State == "running").OrderBy(x => x.StartedAt).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(token);
        if (pending.HasValue) await RunAsync(pending.Value, token);
    }

    /// <summary>DBと画像のコピーが完了するまで書込を止め、圧縮・検証中は更新を再開</summary>
    public async Task RunAsync(Guid id, CancellationToken token)
    {
        await maintenance.Semaphore.WaitAsync(token);
        var stage = Path.Combine(WorkRoot, id.ToString("N")); var destination = Path.Combine(options.DirectoryPath, id.ToString("N") + ".zip");
        try
        {
            BackupManifest manifest;
            await writes.Semaphore.WaitAsync(token);
            try
            {
                db.ChangeTracker.Clear(); var run = await db.Set<BackupRun>().SingleAsync(x => x.Id == id, token);
                if (run.State != "running") return;
                var keys = BackupSnapshot.ReadKeys(runtime.DirectoryPath, maintenance.KeyLock);
                await TrackKeysAsync(keys, token);
                await db.Database.OpenConnectionAsync(token);
                await DatabaseMaintenance.VerifyAsync((SqliteConnection)db.Database.GetDbConnection());
                BackupPaths.RejectLinks(WorkRoot); Directory.CreateDirectory(WorkRoot);
                manifest = await BackupSnapshot.StageAsync((SqliteConnection)db.Database.GetDbConnection(), runtime.DirectoryPath, stage, keys, id, run.Kind, clock.GetUtcNow(), token);
            }
            finally { writes.Semaphore.Release(); }
            await BackupArchive.VerifyDatabaseAsync(stage, manifest, token);
            var hash = await BackupArchive.CreateAsync(stage, destination, manifest, options, token);
            WriteOwned(destination, manifest);
            await writes.Semaphore.WaitAsync(token);
            try
            {
                db.ChangeTracker.Clear(); var run = await db.Set<BackupRun>().SingleAsync(x => x.Id == id, token);
                run.State = "succeeded"; run.CompletedAt = clock.GetUtcNow(); run.SnapshotDataRevision = manifest.DataRevision; run.RelativePath = Path.GetFileName(destination); run.ManifestHash = hash; run.ByteLength = new FileInfo(destination).Length; run.ErrorCode = null;
                await db.SaveChangesAsync(token);
                await new ImageAlerts(db, clock).ResolveAsync("backup:create", token);
                await new ImageAlerts(db, clock).ResolveAsync("backup:interrupted", token);
                foreach (var old in await db.Set<BackupRun>().Where(x => x.Kind == "automatic" && x.RetentionManaged && x.State == "succeeded").OrderByDescending(x => x.CompletedAt).ThenByDescending(x => x.Id).Skip(3).ToArrayAsync(token))
                {
                    old.State = "deleting"; await db.SaveChangesAsync(token); await DeleteFileAsync(old, token);
                }
            }
            finally { writes.Semaphore.Release(); }
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            logger.LogWarning("バックアップ失敗 Id={Id} Code={Code} Type={Type}", id, "BACKUP_FAILED", error.GetType().Name);
            await writes.Semaphore.WaitAsync(CancellationToken.None);
            try
            {
                db.ChangeTracker.Clear(); var run = await db.Set<BackupRun>().SingleAsync(x => x.Id == id);
                if (run.State == "running") { run.State = "failed"; run.CompletedAt = clock.GetUtcNow(); run.ErrorCode = error is OperationCanceledException ? "BACKUP_INTERRUPTED" : "BACKUP_FAILED"; await db.SaveChangesAsync(); }
                await AlertAsync(run.ErrorCode ?? "BACKUP_RETENTION_FAILED", CancellationToken.None);
            }
            finally { writes.Semaphore.Release(); }
        }
        finally
        {
            try { BackupPaths.DeleteTree(WorkRoot, stage); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { logger.LogWarning("バックアップ一時領域の整理待ち Id={Id} Code={Code}", id, "BACKUP_CLEANUP_FAILED"); }
            finally { maintenance.Semaphore.Release(); }
        }
    }

    /// <summary>鍵の変更は保全対象。履歴や実行日時の変更とは区別する</summary>
    private async Task TrackKeysAsync(IReadOnlyDictionary<string, byte[]> keys, CancellationToken token)
    {
        var settings = await db.Set<BackupSettings>().SingleAsync(token); var hash = BackupSnapshot.KeyFingerprint(keys);
        if (settings.KeyFingerprint == hash) return;
        // 初期の鍵を保全対象として変更番号に含め、次回から履歴更新による自己ループを防ぐ。
        settings.KeyFingerprint = hash;
        var state = await db.SystemStates.SingleAsync(token); state.DataRevision = checked(state.DataRevision + 1); state.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(token);
    }

    /// <summary>未知の移行履歴、壊れたDB、画像参照を実際の展開先で検証</summary>
    public async Task<BackupManifest> VerifyStreamAsync(Stream input, CancellationToken token)
    {
        BackupPaths.RejectLinks(WorkRoot); Directory.CreateDirectory(WorkRoot);
        var stage = Path.Combine(WorkRoot, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(stage);
        try
        {
            var manifest = await BackupArchive.VerifyAsync(input, options, stage, token);
            await using var copy = BackupArchive.OpenDatabase(stage, true); await copy.OpenAsync(token);
            var applied = await BackupArchive.StringsAsync(copy, "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId", token);
            if (!applied.SequenceEqual(db.Database.GetMigrations().Take(applied.Length))) throw new InvalidDataException("このアプリで復元できないスキーマ");
            await BackupDatabaseSchema.VerifyAsync(db, copy, manifest.SchemaVersion, token);
            return manifest;
        }
        finally { BackupPaths.DeleteTree(WorkRoot, stage); }
    }

    /// <summary>削除の受付後にファイルを整理。中断しても同じ状態から再開する</summary>
    public async Task ProcessDeletionsAsync(CancellationToken token)
    {
        await maintenance.Semaphore.WaitAsync(token);
        try
        {
            await writes.Semaphore.WaitAsync(token);
            try
            {
                db.ChangeTracker.Clear();
                foreach (var run in await db.Set<BackupRun>().Where(x => x.State == "deleting").ToArrayAsync(token))
                {
                    try { await DeleteFileAsync(run, token); }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException) { logger.LogWarning("バックアップ削除の再試行待ち Id={Id} Code={Code}", run.Id, "BACKUP_DELETE_FAILED"); }
                }
            }
            finally { writes.Semaphore.Release(); }
        }
        finally { maintenance.Semaphore.Release(); }
    }


    /// <summary>履歴の保存先を専用領域内へ限定</summary>
    public string Resolve(BackupRun run)
    {
        if (run.RelativePath is null) throw new InvalidDataException("バックアップの保存先が未確定");
        var root = run.RelativePath.StartsWith("pre-migration/", StringComparison.Ordinal) ? Path.Combine(runtime.DirectoryPath, "pre-migration") : options.DirectoryPath;
        var name = run.RelativePath.StartsWith("pre-migration/", StringComparison.Ordinal) ? run.RelativePath["pre-migration/".Length..] : run.RelativePath;
        if (Path.GetFileName(name) != name || !name.EndsWith(".zip", StringComparison.Ordinal) || name.Contains(':') || name.Contains('\\')) throw new InvalidDataException("バックアップの保存先が不正");
        var path = Path.Combine(root, name); BackupPaths.RejectLinks(path); return path;
    }
    /// <summary>論理削除後の実体を整理し、失敗は再開可能な状態に残す</summary>
    public async Task DeleteFileAsync(BackupRun run, CancellationToken token)
    {
        try
        {
            var path = Resolve(run); File.Delete(path); File.Delete(path + ".owned");
            run.State = "deleted"; run.ErrorCode = null; await db.SaveChangesAsync(token);
            await new ImageAlerts(db, clock).ResolveAsync("backup:archive:" + run.Id.ToString("D"), token);
            if (!await db.Set<BackupRun>().AnyAsync(x => x.State == "deleting", token))
            {
                await new ImageAlerts(db, clock).ResolveAsync("backup:delete", token);
                await new ImageAlerts(db, clock).ResolveAsync("backup:retention", token);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { await AlertAsync("BACKUP_DELETE_FAILED", token); throw; }
    }
    private Task AlertAsync(string code, CancellationToken token) => new ImageAlerts(db, clock).SetAsync(code == "BACKUP_DELETE_FAILED" ? "backup:delete" : code == "BACKUP_RETENTION_FAILED" ? "backup:retention" : code == "BACKUP_ARCHIVE_UNAVAILABLE" ? "backup:archive" : code == "BACKUP_INTERRUPTED" ? "backup:interrupted" : "backup:create", code, Guid.Empty, token);

    /// <summary>外部からコピーしたZIPを自動削除しないため、保存先ごとの所有識別を保持</summary>
    private string Owner()
    {
        var path = Path.Combine(options.DirectoryPath, ".owner-id"); BackupPaths.RejectLinks(path);
        if (!File.Exists(path)) { using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); var bytes = System.Text.Encoding.ASCII.GetBytes(Guid.NewGuid().ToString("N")); stream.Write(bytes); stream.Flush(true); }
        var value = File.ReadAllText(path);
        return Guid.TryParseExact(value, "N", out _) ? value : throw new InvalidDataException("バックアップ所有情報が不正");
    }
    /// <summary>検証済みZIPだけへ所有と内容の対応を記録</summary>
    private void WriteOwned(string path, BackupManifest manifest)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(Owner() + "\n" + ManifestHash(manifest));
        using var output = new FileStream(path + ".owned", FileMode.CreateNew, FileAccess.Write, FileShare.None); output.Write(bytes); output.Flush(true);
    }
    /// <summary>同じ保存先が作成したZIPかを内容付きで確認</summary>
    private bool IsOwned(string path, BackupManifest manifest)
    {
        var sidecar = path + ".owned"; BackupPaths.RejectLinks(sidecar);
        return File.Exists(sidecar) && new FileInfo(sidecar).Length < 200 && File.ReadAllText(sidecar) == Owner() + "\n" + ManifestHash(manifest);
    }
}

/// <summary>手動受付を即時通知し、日次判定と実行を一つのワーカーで直列化</summary>
public sealed class BackupSignal { public SemaphoreSlim Semaphore { get; } = new(0, 1); public void Wake() { if (Semaphore.CurrentCount == 0) try { Semaphore.Release(); } catch (SemaphoreFullException) { } } }
public sealed class BackupWorker(IServiceScopeFactory scopes, BackupOptions options, BackupSignal signal, ILogger<BackupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        if (!options.WorkerEnabled) return;
        while (!token.IsCancellationRequested)
        {
            try { await using var scope = scopes.CreateAsyncScope(); await scope.ServiceProvider.GetRequiredService<BackupService>().TickAsync(token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception error) { logger.LogWarning("バックアップワーカー待機 Code={Code} Type={Type}", "BACKUP_WORKER_FAILED", error.GetType().Name); }
            try { await signal.Semaphore.WaitAsync(TimeSpan.FromSeconds(30), token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
        }
    }
}
