using System.Security.Cryptography;
using System.Text;
using GoodsManager.Web.Images;
using GoodsManager.Web.Storage;
using Microsoft.Data.Sqlite;

namespace GoodsManager.Web.Backups;

/// <summary>書込停止中に不変の一式を準備し、ZIP圧縮は停止区間の外で行う</summary>
public static class BackupSnapshot
{
    /// <summary>鍵の書込と列挙を同じロックで調整し、内容から変更検知値を求める</summary>
    public static IReadOnlyDictionary<string, byte[]> ReadKeys(string dataDirectory, object keyLock)
    {
        lock (keyLock)
        {
            var directory = Path.Combine(dataDirectory, "keys"); BackupPaths.RejectLinks(directory);
            var keys = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
            if (Directory.Exists(directory)) foreach (var path in Directory.EnumerateFiles(directory))
            {
                BackupPaths.RejectLinks(path); var name = "keys/" + Path.GetFileName(path);
                if (!BackupArchive.ValidPath(name)) throw new InvalidDataException("鍵の保存名が不正");
                keys.Add(name, File.ReadAllBytes(path));
            }
            return keys;
        }
    }
    public static string KeyFingerprint(IReadOnlyDictionary<string, byte[]> keys)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var pair in keys.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(pair.Key)); hash.AppendData([0]); hash.AppendData(SHA256.HashData(pair.Value));
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    /// <summary>稼働DBの直接コピーを避け、SQLiteのバックアップAPIとコピー側の整理を使用</summary>
    public static async Task<BackupManifest> StageAsync(SqliteConnection source, string dataDirectory, string stage,
        IReadOnlyDictionary<string, byte[]> keys, Guid id, string kind, DateTimeOffset now, CancellationToken token)
    {
        if (Directory.Exists(stage)) throw new IOException("スナップショット領域が既に存在");
        BackupPaths.RejectLinks(stage); Directory.CreateDirectory(stage);
        var path = Path.Combine(stage, "goods-manager.db");
        await using (var copy = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false, ForeignKeys = true }.ToString()))
        {
            await copy.OpenAsync(token); source.BackupDatabase(copy);
            await SanitizeAsync(copy, token);
            // WALを展開先へ持ち越さず、検証済みの単独DBへ確定する。
            await BackupArchive.ScalarAsync(copy, "PRAGMA journal_mode=DELETE", token);
            await DatabaseMaintenance.VerifyAsync(copy);
        }
        long revision; string schema;
        await using (var copy = BackupArchive.OpenDatabase(stage, readOnly: true))
        {
            await copy.OpenAsync(token);
            revision = Convert.ToInt64(await BackupArchive.ScalarAsync(copy, "SELECT DataRevision FROM SystemState WHERE Id=1", token));
            schema = (await BackupArchive.StringsAsync(copy, "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId", token)).Last();
            using var runtime = new DatabaseRuntime(dataDirectory); var files = new ImageFiles(runtime);
            foreach (var file in await BackupArchive.ImageFilesAsync(copy, token))
            {
                await files.VerifyAsync(file, token);
                var destination = Path.Combine(stage, "images", file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                await using var input = File.OpenRead(files.Resolve(file.RelativePath));
                await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                if (await BackupArchive.CopyHashAsync(input, output, file.ByteLength, token) != file.Sha256) throw new InvalidDataException("画像のコピー検証に失敗");
            }
        }
        foreach (var key in keys)
        {
            var destination = Path.Combine(stage, key.Key); Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await output.WriteAsync(key.Value, token); output.Flush(true);
        }
        var entries = new List<BackupFile>();
        foreach (var file in Directory.EnumerateFiles(stage, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(stage, file).Replace('\\', '/');
            await using var input = File.OpenRead(file);
            entries.Add(new(relative, input.Length, await BackupArchive.CopyHashAsync(input, null, input.Length, token)));
        }
        return new(1, typeof(BackupSnapshot).Assembly.GetName().Version?.ToString() ?? "1.0", schema, revision, now.ToUniversalTime(), id, kind, entries);
    }

    /// <summary>本体DBを変更せず、復元できない一時状態と到達しない画像をコピー側だけから除く</summary>
    public static async Task SanitizeAsync(SqliteConnection copy, CancellationToken token)
    {
        foreach (var table in new[] { "AuthTickets", "AuthChallenges", "IdempotencyRecords", "TemporaryUploads", "BackupRuns", "SystemAlerts" })
            if (await BackupArchive.HasTableAsync(copy, table, token)) await BackupArchive.ScalarAsync(copy, $"DELETE FROM {table}", token);
        if (await BackupArchive.HasTableAsync(copy, "ImageAssets", token))
        {
            await BackupArchive.ScalarAsync(copy, """
                DELETE FROM ImageAssets WHERE NOT EXISTS(SELECT 1 FROM ProductImages p WHERE p.ImageAssetId=ImageAssets.ImageAssetId)
                  AND NOT EXISTS(SELECT 1 FROM InventoryItemImages i WHERE i.ImageAssetId=ImageAssets.ImageAssetId);
                DELETE FROM ImageFiles WHERE NOT EXISTS(SELECT 1 FROM ImageAssets a WHERE ImageFiles.ImageFileId IN (a.DisplayFileId,a.ThumbnailFileId,a.OriginalFileId));
                """, token);
        }
    }
}
