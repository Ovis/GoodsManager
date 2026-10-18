using System.IO.Compression;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GoodsManager.Web.Storage;

/// <summary>起動前のDB検査と、更新前退避を伴うマイグレーション</summary>
public static class DatabaseMaintenance
{
    /// <summary>JSON関数と整合性を検証し、更新失敗を呼出元へ伝えて起動を止める</summary>
    public static async Task MigrateAsync(DbContext context, string dataDirectory)
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
                    CreateSnapshot(connection, dataDirectory);
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

    /// <summary>WALを含む一貫したDBと鍵をZIPへ退避し、完成前のファイルと区別</summary>
    private static void CreateSnapshot(SqliteConnection source, string dataDirectory)
    {
        var backupDirectory = Path.Combine(dataDirectory, "pre-migration");
        Directory.CreateDirectory(backupDirectory);
        var stem = Path.Combine(backupDirectory, $"{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}");
        var snapshot = stem + ".db";
        try
        {
            using (var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = snapshot, Pooling = false }.ToString()))
            {
                target.Open();
                source.BackupDatabase(target);
                VerifyAsync(target).GetAwaiter().GetResult();
            }

            using (var zip = ZipFile.Open(stem + ".partial", ZipArchiveMode.Create))
            {
                zip.CreateEntryFromFile(snapshot, "goods-manager.db");
                var keys = Path.Combine(dataDirectory, "keys");
                if (Directory.Exists(keys))
                {
                    foreach (var key in Directory.EnumerateFiles(keys))
                    {
                        zip.CreateEntryFromFile(key, "keys/" + Path.GetFileName(key));
                    }
                }
            }

            File.Move(stem + ".partial", stem + ".zip");
        }
        finally
        {
            File.Delete(snapshot);
        }
    }
}
