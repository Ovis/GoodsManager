using System.Text.Json;
using GoodsManager.Web.Storage;
using Microsoft.EntityFrameworkCore;

namespace GoodsManager.Web.Backups;

public sealed record RestoreJournal(Guid Id, string Phase, IReadOnlyList<string> Existing);

/// <summary>停止中の復元。複数の改名を一体と扱わず、進行記録から旧一式へ戻せるようにする</summary>
public sealed class BackupRestore(DatabaseRuntime runtime, BackupOptions options)
{
    private static readonly string[] Members = ["goods-manager.db", "goods-manager.db-wal", "goods-manager.db-shm", "images", "keys"];
    public string Marker => Path.Combine(runtime.DirectoryPath, "restore-state.json");
    private string Stage(Guid id) => Path.Combine(runtime.DirectoryPath, ".restore-" + id.ToString("N"));
    private string Previous(Guid id) => Path.Combine(runtime.DirectoryPath, ".rollback-" + id.ToString("N"));

    /// <summary>別領域でZIP・互換性・DB参照を検証。既存データを切り替える前に全条件を満たす</summary>
    public async Task<BackupManifest> VerifyAsync(string archive, string stage, CancellationToken token)
    {
        BackupPaths.RejectLinks(archive); BackupPaths.RejectLinks(stage);
        if (Directory.Exists(stage)) throw new IOException("復元の準備領域が既に存在");
        Directory.CreateDirectory(stage);
        await using var input = File.OpenRead(archive);
        var manifest = await BackupArchive.VerifyAsync(input, options, stage, token);
        await using var copy = BackupArchive.OpenDatabase(stage, true); await copy.OpenAsync(token);
        await using var metadata = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(runtime.ConnectionString).Options);
        var known = metadata.Database.GetMigrations().ToArray();
        var applied = await BackupArchive.StringsAsync(copy, "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId", token);
        if (applied.Length == 0 || !applied.SequenceEqual(known.Take(applied.Length))) throw new InvalidDataException("このアプリで復元できないDB");
        await BackupDatabaseSchema.VerifyAsync(metadata, copy, manifest.SchemaVersion, token);
        return manifest;
    }

    /// <summary>切替前に現データを退避し、途中例外では同じ一式へ戻す</summary>
    public async Task<Guid> RestoreAsync(string archive, CancellationToken token, Action<string>? boundary = null)
    {
        if (File.Exists(Marker)) throw new InvalidOperationException("復元の進行記録が残っているため先にrollbackが必要");
        var id = Guid.NewGuid(); var stage = Stage(id); var previous = Previous(id);
        var journalWritten = false;
        try
        {
            await VerifyAsync(archive, stage, token);
            var stagedConnection = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(new DatabaseRuntime(stage).ConnectionString) { Pooling = false }.ToString();
            await using (var copy = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(stagedConnection).Options))
            {
                // 旧版は準備領域の中だけで移行する。失敗しても本体には触れない。
                await DatabaseMaintenance.MigrateAsync(copy, stage, options);
                await copy.Database.OpenConnectionAsync(token);
                var connection = (Microsoft.Data.Sqlite.SqliteConnection)copy.Database.GetDbConnection();
                await BackupSnapshot.SanitizeAsync(connection, token);
                await VerifyCurrentModelAsync(copy, token);
                await BackupArchive.ScalarAsync(connection, "UPDATE AspNetUsers SET SecurityStamp=lower(hex(randomblob(32))),ConcurrencyStamp=lower(hex(randomblob(16))); UPDATE SystemState SET DataRevision=DataRevision+1; UPDATE BackupSettings SET KeyFingerprint=NULL", token);
                await BackupArchive.ScalarAsync(connection, "PRAGMA wal_checkpoint(TRUNCATE)", token);
                await BackupArchive.ScalarAsync(connection, "PRAGMA journal_mode=DELETE", token);
                await DatabaseMaintenance.VerifyAsync(connection);
            }
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            // 壊れた現DBでも復元を妨げない。WAL・SHMも退避し、旧一式を変更せず残す。
            var existing = Members.Where(name => File.Exists(Path.Combine(runtime.DirectoryPath, name)) || Directory.Exists(Path.Combine(runtime.DirectoryPath, name))).ToArray();
            foreach (var name in Members) BackupPaths.RejectLinks(Path.Combine(runtime.DirectoryPath, name));
            Directory.CreateDirectory(previous);
            SaveJournal(new(id, "switching", existing)); journalWritten = true;
            boundary?.Invoke("prepared");
            foreach (var name in Members)
            {
                var current = Path.Combine(runtime.DirectoryPath, name); var old = Path.Combine(previous, name); var next = Path.Combine(stage, name);
                if (existing.Contains(name)) Move(current, old);
                boundary?.Invoke("old:" + name);
                if (File.Exists(next) || Directory.Exists(next)) Move(next, current);
                boundary?.Invoke("new:" + name);
            }
            await using (var current = BackupArchive.OpenDatabase(runtime.DirectoryPath, true)) { await current.OpenAsync(token); await DatabaseMaintenance.VerifyAsync(current); }
            SaveJournal(new(id, "committed", existing));
            boundary?.Invoke("committed");
            // 退避一式は成功後も残し、自動で破棄しない。
            File.Copy(Marker, Path.Combine(previous, "receipt.json"), overwrite: false);
            File.Delete(Marker); journalWritten = false;
            return id;
        }
        catch
        {
            if (journalWritten) Rollback();
            throw;
        }
        finally { if (!File.Exists(Marker)) BackupPaths.DeleteTree(runtime.DirectoryPath, stage); }
    }

    /// <summary>各改名の直後に停止しても、退避の有無から元の構成を復元</summary>
    public void Rollback()
    {
        BackupPaths.RejectLinks(Marker);
        if (!File.Exists(Marker)) throw new InvalidOperationException("未完了の復元記録が存在しない");
        if (new FileInfo(Marker).Length > 4096) throw new InvalidDataException("復元記録の容量が不正");
        var journal = JsonSerializer.Deserialize<RestoreJournal>(File.ReadAllBytes(Marker), BackupArchive.Json) ?? throw new InvalidDataException("復元記録が不正");
        if (journal.Id == Guid.Empty || journal.Existing is null || journal.Existing.Distinct().Count() != journal.Existing.Count || journal.Existing.Except(Members).Any() || journal.Phase is not ("switching" or "committed"))
            throw new InvalidDataException("復元記録の内容が不正");
        if (journal.Phase == "committed")
        {
            // 切替完了記録の後の停止は、退避を残して記録を解除するだけにする。
            var receipt = Path.Combine(Previous(journal.Id), "receipt.json");
            BackupPaths.RejectLinks(receipt);
            if (!File.Exists(receipt)) File.Copy(Marker, receipt);
            File.Delete(Marker); BackupPaths.DeleteTree(runtime.DirectoryPath, Stage(journal.Id)); return;
        }
        foreach (var name in Members)
        {
            var current = Path.Combine(runtime.DirectoryPath, name); var old = Path.Combine(Previous(journal.Id), name);
            BackupPaths.RejectLinks(current); BackupPaths.RejectLinks(old);
            if (File.Exists(old) || Directory.Exists(old))
            {
                RemoveMember(current); Move(old, current);
            }
            else if (!journal.Existing.Contains(name)) RemoveMember(current);
        }
        File.Delete(Marker); BackupPaths.DeleteTree(runtime.DirectoryPath, Stage(journal.Id));
    }

    /// <summary>履歴だけを偽装したDBを切り替えず、現行モデルの全列と検索再構築も準備領域で確認</summary>
    private static async Task VerifyCurrentModelAsync(AppDbContext context, CancellationToken token)
    {
        var connection = (Microsoft.Data.Sqlite.SqliteConnection)context.Database.GetDbConnection();
        foreach (var table in context.Model.GetRelationalModel().Tables)
        {
            using var command = connection.CreateCommand(); command.CommandText = "SELECT name,type FROM pragma_table_info($table)"; command.Parameters.AddWithValue("$table", table.Name);
            var columns = new Dictionary<string, string>(StringComparer.Ordinal);
            await using (var reader = await command.ExecuteReaderAsync(token)) while (await reader.ReadAsync(token)) columns.Add(reader.GetString(0), reader.GetString(1));
            if (table.Columns.Any(column => !columns.TryGetValue(column.Name, out var type) || !string.Equals(type, column.StoreType, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("移行履歴とDBの実際の列構成が不一致");
        }
        await new GoodsManager.Web.Search.SearchProjection(context).RebuildAllAsync(token);
    }


    /// <summary>進行記録で許可した構成要素だけを切替・取消</summary>
    private void RemoveMember(string path)
    {
        if (Path.GetDirectoryName(path) != runtime.DirectoryPath || !Members.Contains(Path.GetFileName(path))) throw new InvalidDataException("復元対象の範囲が不正");
        if (Directory.Exists(path)) BackupPaths.DeleteTree(runtime.DirectoryPath, path);
        else File.Delete(path);
    }
    /// <summary>リンクを辿らず、上書きしない改名で切り替える</summary>
    private static void Move(string source, string target)
    {
        BackupPaths.RejectLinks(source); BackupPaths.RejectLinks(target);
        if (Directory.Exists(source)) Directory.Move(source, target); else File.Move(source, target);
    }
    /// <summary>同期書込した進行記録を公開してから構成要素を動かす</summary>
    private void SaveJournal(RestoreJournal journal)
    {
        var temporary = Marker + ".partial"; BackupPaths.RejectLinks(temporary); BackupPaths.RejectLinks(Marker);
        using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        { JsonSerializer.Serialize(output, journal, BackupArchive.Json); output.Flush(true); }
        File.Move(temporary, Marker, overwrite: true);
    }
}

/// <summary>HTTPへ復元経路を作らず、停止中の同じ永続領域を排他的に扱う</summary>
public static class BackupCli
{
    /// <summary>引数を検証し、Webホストや認証設定を起動せず実行</summary>
    public static async Task<int> RunAsync(IConfiguration configuration, string contentRoot, string[] args)
    {
        using var runtime = new DatabaseRuntime(configuration["Storage:Directory"] ?? Path.Combine(contentRoot, "data"));
        var options = new BackupOptions(configuration, runtime);
        try
        {
            if (args.Length == 0 || args[0] is not ("create" or "verify" or "restore" or "rollback")) return Usage();
            if (args[0] == "rollback" && (args.Length != 2 || args[1] != "--confirm-rollback") ||
                args[0] == "restore" && (args.Length != 3 || args[2] != "--confirm-replace") ||
                args[0] == "verify" && args.Length != 2 || args[0] == "create" && args.Length != 1) return Usage();
            runtime.AcquireLease(allowInterruptedRestore: args[0] == "rollback");
            var restore = new BackupRestore(runtime, options);
            if (args[0] == "rollback") { restore.Rollback(); Console.WriteLine("未完了の復元を処理。再起動前に内容を確認してください。"); return 0; }
            if (args[0] == "restore")
            {
                var id = await restore.RestoreAsync(Path.GetFullPath(args[1]), default);
                Console.WriteLine($"復元完了。退避ID: {id:N}。全端末で再ログイン・画面再読み込みを行い、復元前の結果不明要求を再送しないでください。"); return 0;
            }
            if (args[0] == "verify")
            {
                var stage = Path.Combine(runtime.DirectoryPath, ".verify-" + Guid.NewGuid().ToString("N"));
                try { var manifest = await restore.VerifyAsync(Path.GetFullPath(args[1]), stage, default); Console.WriteLine($"検証成功。実行ID: {manifest.RunId:D} / データ変更番号: {manifest.DataRevision}"); }
                finally { BackupPaths.DeleteTree(runtime.DirectoryPath, stage); }
                return 0;
            }
            var services = new ServiceCollection().AddLogging().AddSingleton(configuration).AddSingleton(runtime).AddSingleton(options).AddSingleton<BackupGate>().AddSingleton<GoodsManager.Web.Auth.AuthWriteGate>().AddSingleton(TimeProvider.System)
                .AddDbContext<AppDbContext>(o => o.UseSqlite(runtime.ConnectionString)).AddScoped<BackupService>().BuildServiceProvider();
            await using (services)
            {
                await using var scope = services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                if (!File.Exists(Path.Combine(runtime.DirectoryPath, "goods-manager.db")) || await DatabaseMaintenance.HasPendingMigrationsAsync(db)) throw new InvalidOperationException("対応するアプリでDB更新を完了してから作成する");
                var run = new BackupRun { Id = Guid.NewGuid(), StartedAt = DateTimeOffset.UtcNow, RetentionManaged = true };
                db.Add(run); await db.SaveChangesAsync(); await scope.ServiceProvider.GetRequiredService<BackupService>().RunAsync(run.Id, default);
                db.ChangeTracker.Clear(); run = await db.Set<BackupRun>().SingleAsync(x => x.Id == run.Id);
                if (run.State != "succeeded") throw new IOException("バックアップに失敗");
                Console.WriteLine($"作成成功。実行ID: {run.Id:D} / ZIP: {Path.Combine(options.DirectoryPath, run.RelativePath!)}"); return 0;
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        { Console.Error.WriteLine($"保全処理を完了できませんでした（{error.GetType().Name}）。原本と退避一式を保持し、パス・権限・ZIP・対応版・復元記録を確認してください。"); return 1; }
    }
    /// <summary>秘密をコマンド引数に要求しない使用方法</summary>
    private static int Usage()
    {
        Console.Error.WriteLine("停止中に backup create | backup verify ZIP | backup restore ZIP --confirm-replace | backup rollback --confirm-rollback を指定してください。"); return 2;
    }
}
