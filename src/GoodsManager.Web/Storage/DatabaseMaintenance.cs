using System.IO.Compression;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GoodsManager.Web.Storage;

/// <summary>起動前のDB検査と、更新前退避を伴うマイグレーション</summary>
public static class DatabaseMaintenance
{
    /// <summary>JSON関数と整合性を検証し、更新失敗を呼出元へ伝えて起動を止める</summary>
    public static async Task MigrateAsync(DbContext context, string dataDirectory, GoodsManager.Web.Backups.BackupOptions? backupOptions = null, object? keyLock = null)
    {
        await context.Database.OpenConnectionAsync();
        try
        {
            var connection = (SqliteConnection)context.Database.GetDbConnection();
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT json_valid('{\"test\":1}')";
                if (Convert.ToInt64(await command.ExecuteScalarAsync()) != 1)
                {
                    throw new InvalidOperationException("JSON関数が利用できない");
                }

                command.CommandText = "PRAGMA journal_mode=WAL";
                if (!string.Equals((string?)await command.ExecuteScalarAsync(), "wal", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("WALを有効にできない");
                }
            }

            await VerifyAsync(connection);
            if (await HasPendingMigrationsAsync(context))
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'";
                if (Convert.ToInt64(await command.ExecuteScalarAsync()) > 0)
                {
                    if (backupOptions is null) CreateSnapshot(connection, dataDirectory);
                    else CreateFullSnapshot(connection, dataDirectory, backupOptions, keyLock ?? new object());
                }

                // 検証済みの退避が完成してから移行する。移行中の例外は握り潰さない
                await context.Database.MigrateAsync();
            }

            await VerifyAsync(connection);
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    /// <summary>未知・順序不整合の移行履歴と残留ロックを拒否し、未適用の有無を返す</summary>
    public static async Task<bool> HasPendingMigrationsAsync(DbContext context, CancellationToken cancellationToken = default)
    {
        var known = context.Database.GetMigrations().ToArray();
        var applied = (await context.Database.GetAppliedMigrationsAsync(cancellationToken)).ToArray();
        if (!applied.SequenceEqual(known.Take(applied.Length)))
        {
            throw new InvalidOperationException("DBの移行履歴がこのアプリの対応範囲と一致しない");
        }

        var connection = (SqliteConnection)context.Database.GetDbConnection();
        var openedHere = connection.State != System.Data.ConnectionState.Open;
        if (openedHere)
        {
            await context.Database.OpenConnectionAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='__EFMigrationsLock'";
            if (Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) != 0)
            {
                command.CommandText = "SELECT count(*) FROM __EFMigrationsLock WHERE Id=1";
                if (Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) != 0)
                {
                    // アプリ/CLIの排他取得後でもロック行が残る場合は、勝手に消して移行を再開しない
                    throw new InvalidOperationException("DBに未解放の移行ロックが残っているため起動できない");
                }
            }
        }
        finally
        {
            if (openedHere)
            {
                await context.Database.CloseConnectionAsync();
            }
        }

        return applied.Length < known.Length;
    }

    /// <summary>SQLiteの整合性と外部キー違反を検査</summary>
    public static async Task VerifyAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check";
        if ((string?)await command.ExecuteScalarAsync() != "ok")
        {
            throw new InvalidOperationException("DB整合性検査に失敗");
        }

        command.CommandText = "PRAGMA foreign_key_check";
        await using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            throw new InvalidOperationException("DBに外部キー違反が存在");
        }
    }

    /// <summary>旧呼出元も完成版のマニフェスト付きZIPへ統一</summary>
    private static void CreateSnapshot(SqliteConnection source, string dataDirectory)
    {
        using var runtime = new DatabaseRuntime(dataDirectory);
        var options = new GoodsManager.Web.Backups.BackupOptions(new ConfigurationBuilder().Build(), runtime);
        CreateFullSnapshot(source, dataDirectory, options, new object());
    }

    /// <summary>移行前のDB・正式画像・鍵を検証してから、上書きしないZIPとして公開</summary>
    public static void CreateFullSnapshot(SqliteConnection source, string dataDirectory, GoodsManager.Web.Backups.BackupOptions options, object keyLock)
    {
        var directory = Path.Combine(dataDirectory, "pre-migration");
        GoodsManager.Web.Backups.BackupPaths.RejectLinks(directory); Directory.CreateDirectory(directory);
        var id = Guid.NewGuid(); var stage = Path.Combine(directory, id.ToString("N"));
        try
        {
            var keys = GoodsManager.Web.Backups.BackupSnapshot.ReadKeys(dataDirectory, keyLock);
            var manifest = GoodsManager.Web.Backups.BackupSnapshot.StageAsync(source, dataDirectory, stage, keys, id, "preMigration", DateTimeOffset.UtcNow, default).GetAwaiter().GetResult();
            GoodsManager.Web.Backups.BackupArchive.VerifyDatabaseAsync(stage, manifest, default).GetAwaiter().GetResult();
            GoodsManager.Web.Backups.BackupArchive.CreateAsync(stage, Path.Combine(directory, id.ToString("N") + ".zip"), manifest, options, default).GetAwaiter().GetResult();
        }
        finally { GoodsManager.Web.Backups.BackupPaths.DeleteTree(directory, stage); }
    }
}
