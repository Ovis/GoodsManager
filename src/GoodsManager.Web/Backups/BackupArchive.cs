using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using GoodsManager.Web.Images;
using GoodsManager.Web.Storage;
using Microsoft.Data.Sqlite;

namespace GoodsManager.Web.Backups;

public sealed record BackupFile(string Path, long Size, string Sha256);
public sealed record BackupManifest(int FormatVersion, string AppVersion, string SchemaVersion, long DataRevision, DateTimeOffset CreatedAt, Guid RunId, string Kind, IReadOnlyList<BackupFile> Files);

/// <summary>サイズ・パス・全ファイルのハッシュとDB参照を検証する共通ZIP形式</summary>
public static partial class BackupArchive
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, RespectRequiredConstructorParameters = true };
    [GeneratedRegex("^(goods-manager\\.db|keys/[a-zA-Z0-9_.-]+\\.xml|images/objects/[0-9a-f]{2}/[0-9a-f]{64})$", RegexOptions.CultureInvariant)]
    private static partial Regex EntryPattern();
    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex HashPattern();

    /// <summary>完成前の一時ZIPへ書き、同じ検証を通してから公開する</summary>
    public static async Task<string> CreateAsync(string stage, string destination, BackupManifest manifest, BackupOptions options, CancellationToken token)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(manifest, Json);
        if (bytes.Length > options.MaximumManifestBytes || manifest.Files.Count + 1 > options.MaximumFiles) throw new InvalidDataException("バックアップ上限を超過");
        var partial = destination + ".partial";
        if (File.Exists(partial) || File.Exists(destination)) throw new IOException("バックアップの保存先が既に存在");
        BackupPaths.RejectLinks(destination);
        try
        {
            await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
                {
                    await using (var entry = zip.CreateEntry("manifest.json", CompressionLevel.Optimal).Open()) await entry.WriteAsync(bytes, token);
                    foreach (var file in manifest.Files)
                    {
                        token.ThrowIfCancellationRequested();
                        if (!ValidPath(file.Path)) throw new InvalidDataException("バックアップのパスが不正");
                        await using var input = File.OpenRead(Path.Combine(stage, file.Path.Replace('/', Path.DirectorySeparatorChar)));
                        await using var entry = zip.CreateEntry(file.Path, CompressionLevel.NoCompression).Open();
                        await input.CopyToAsync(entry, token);
                        if (output.Length > options.MaximumArchiveBytes) throw new InvalidDataException("ZIP容量の上限を超過");
                    }
                }
                output.Flush(true);
            }
            await using (var input = File.OpenRead(partial)) await VerifyAsync(input, options, null, token);
            File.Move(partial, destination);
            return Convert.ToHexStringLower(SHA256.HashData(bytes));
        }
        finally { File.Delete(partial); }
    }

    /// <summary>DB・鍵・ハッシュ付き正式画像だけを許可</summary>
    public static bool ValidPath(string path) => path is not null && EntryPattern().IsMatch(path) && !path.Contains("..", StringComparison.Ordinal);

    /// <summary>展開時にもストリームを上限監視し、中央ディレクトリの値だけを信用しない</summary>
    public static async Task<BackupManifest> VerifyAsync(Stream input, BackupOptions options, string? extractTo, CancellationToken token)
    {
        if (!input.CanSeek || input.Length > options.MaximumArchiveBytes) throw new InvalidDataException("ZIP容量の上限を超過");
        BackupZipDirectory.Check(input, options, token);
        input.Position = 0;
        using var zip = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
        if (zip.Entries.Count < 2 || zip.Entries.Count > options.MaximumFiles) throw new InvalidDataException("ZIP件数の上限を超過");
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long expanded = 0;
        foreach (var entry in zip.Entries)
        {
            if (!names.Add(entry.FullName) || (entry.FullName != "manifest.json" && !ValidPath(entry.FullName)) ||
                ((entry.ExternalAttributes >> 16) & 0xF000) is not (0 or 0x8000) || (entry.ExternalAttributes & 0x10) != 0)
                throw new InvalidDataException("ZIPに重複・領域外パス・リンクが存在");
            expanded = checked(expanded + entry.Length);
            if (expanded > options.MaximumExpandedBytes) throw new InvalidDataException("展開容量の上限を超過");
            entries.Add(entry.FullName, entry);
        }
        if (!entries.TryGetValue("manifest.json", out var metadata) || metadata.Length > options.MaximumManifestBytes)
            throw new InvalidDataException("マニフェストが存在しないか上限を超過");
        BackupManifest manifest;
        await using (var content = metadata.Open())
        {
            var data = await ReadBoundedAsync(content, options.MaximumManifestBytes, token);
            try { manifest = JsonSerializer.Deserialize<BackupManifest>(data, Json) ?? throw new InvalidDataException("マニフェストが空"); }
            catch (JsonException e) { throw new InvalidDataException("マニフェストが不正", e); }
        }
        if (manifest.FormatVersion != 1 || manifest.RunId == Guid.Empty || manifest.DataRevision < 0 || manifest.CreatedAt.Offset != TimeSpan.Zero ||
            string.IsNullOrEmpty(manifest.AppVersion) || string.IsNullOrEmpty(manifest.SchemaVersion) ||
            manifest.Kind is not ("automatic" or "manual" or "preMigration") || manifest.Files is null || manifest.Files.Count != entries.Count - 1)
            throw new InvalidDataException("マニフェストの形式が未対応または不正");
        var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in manifest.Files)
        {
            if (file is null || !ValidPath(file.Path) || !declared.Add(file.Path) || file.Size < 0 || (file.Sha256 is null || !HashPattern().IsMatch(file.Sha256)) ||
                !entries.TryGetValue(file.Path, out var entry) || entry.Length != file.Size)
                throw new InvalidDataException("ZIPとマニフェストが不一致");
            Stream? output = null;
            try
            {
                if (extractTo is not null)
                {
                    var path = Path.Combine(extractTo, file.Path.Replace('/', Path.DirectorySeparatorChar));
                    BackupPaths.RejectLinks(path); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                }
                await using var source = entry.Open();
                var hash = await CopyHashAsync(source, output, file.Size, token);
                if (hash != file.Sha256) throw new InvalidDataException("バックアップのハッシュが不一致");
            }
            finally { if (output is not null) await output.DisposeAsync(); }
        }
        if (!declared.Contains("goods-manager.db")) throw new InvalidDataException("バックアップDBが存在しない");
        if (extractTo is not null) await VerifyDatabaseAsync(extractTo, manifest, token, options.MaximumManifestBytes);
        return manifest;
    }

    /// <summary>展開後DBの整合性・変更番号と、正式な全画像参照のマニフェスト内包含を検査</summary>
    public static async Task VerifyDatabaseAsync(string root, BackupManifest manifest, CancellationToken token, int maximumMetadataBytes = 16 * 1024 * 1024)
    {
        await using var db = OpenDatabase(root, readOnly: true); await db.OpenAsync(token);
        await DatabaseMaintenance.VerifyAsync(db);
        var schema = await StringsAsync(db, "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId", token);
        if (schema.LastOrDefault() != manifest.SchemaVersion || Convert.ToInt64(await ScalarAsync(db, "SELECT DataRevision FROM SystemState WHERE Id=1", token)) != manifest.DataRevision)
            throw new InvalidDataException("DBとマニフェストの版が不一致");
        var expected = manifest.Files.ToDictionary(x => x.Path, StringComparer.Ordinal);
        foreach (var file in await ImageFilesAsync(db, token))
        {
            if (!expected.TryGetValue("images/" + file.RelativePath, out var entry) || entry.Size != file.ByteLength || entry.Sha256 != file.Sha256)
                throw new InvalidDataException("正式画像の参照がマニフェストに収まらない");
        }
        foreach (var file in manifest.Files.Where(x => x.Path.StartsWith("keys/", StringComparison.Ordinal)))
        {
            using var reader = System.Xml.XmlReader.Create(Path.Combine(root, file.Path), new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = maximumMetadataBytes });
            var xml = XDocument.Load(reader);
            if (xml.Root?.Name.LocalName is not ("key" or "revocation")) throw new InvalidDataException("鍵ファイルの形式が不正");
        }
    }

    /// <summary>準備領域の接続をプールへ戻さず確実に解放</summary>
    public static SqliteConnection OpenDatabase(string root, bool readOnly = false) => new(new SqliteConnectionStringBuilder { DataSource = Path.Combine(root, "goods-manager.db"), Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWrite, Pooling = false, ForeignKeys = true }.ToString());

    /// <summary>旧版スキーマの任意テーブルを安全に判定</summary>
    public static async Task<bool> HasTableAsync(SqliteConnection db, string table, CancellationToken token)
    {
        using var command = db.CreateCommand(); command.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name=$table"; command.Parameters.AddWithValue("$table", table);
        return Convert.ToInt64(await command.ExecuteScalarAsync(token)) > 0;
    }
    /// <summary>保全処理内の固定SQLを実行</summary>
    public static async Task<object?> ScalarAsync(SqliteConnection db, string sql, CancellationToken token)
    {
        using var command = db.CreateCommand(); command.CommandText = sql; return await command.ExecuteScalarAsync(token);
    }
    /// <summary>移行履歴等を保存順序に従って読み取る</summary>
    public static async Task<string[]> StringsAsync(SqliteConnection db, string sql, CancellationToken token)
    {
        using var command = db.CreateCommand(); command.CommandText = sql; await using var reader = await command.ExecuteReaderAsync(token);
        var values = new List<string>(); while (await reader.ReadAsync(token)) values.Add(reader.GetString(0)); return values.ToArray();
    }
    /// <summary>コピーDBの全画像管理行を読み、保存パスとハッシュの対応を検査</summary>
    public static async Task<ImageFile[]> ImageFilesAsync(SqliteConnection db, CancellationToken token)
    {
        if (!await HasTableAsync(db, "ImageFiles", token)) return [];
        using var command = db.CreateCommand(); command.CommandText = "SELECT ImageFileId,Sha256,RelativePath,ByteLength FROM ImageFiles";
        await using var reader = await command.ExecuteReaderAsync(token); var files = new List<ImageFile>();
        while (await reader.ReadAsync(token))
        {
            var hash = reader.GetString(1); var path = reader.GetString(2);
            if (!HashPattern().IsMatch(hash) || path != ImageFiles.ObjectPath(hash)) throw new InvalidDataException("画像の管理パスが不正");
            files.Add(new ImageFile { Id = StorageValues.ParseId(reader.GetString(0)), Sha256 = hash, RelativePath = path, ByteLength = reader.GetInt64(3) });
        }
        return files.ToArray();
    }

    /// <summary>マニフェストの宣言長が偽装されても上限内に制限</summary>
    private static async Task<byte[]> ReadBoundedAsync(Stream stream, int maximum, CancellationToken token)
    {
        using var output = new MemoryStream(); var buffer = new byte[65536];
        while (true) { var count = await stream.ReadAsync(buffer, token); if (count == 0) break; if (output.Length + count > maximum) throw new InvalidDataException("マニフェスト容量の上限を超過"); await output.WriteAsync(buffer.AsMemory(0, count), token); }
        return output.ToArray();
    }
    /// <summary>一括メモリ展開せず、長さを監視しながらコピーとハッシュ計算</summary>
    public static async Task<string> CopyHashAsync(Stream source, Stream? output, long expectedSize, CancellationToken token)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); var buffer = new byte[65536]; long total = 0;
        while (true)
        {
            var count = await source.ReadAsync(buffer, token); if (count == 0) break; total = checked(total + count);
            if (total > expectedSize) throw new InvalidDataException("ファイル容量の上限を超過");
            hash.AppendData(buffer, 0, count); if (output is not null) await output.WriteAsync(buffer.AsMemory(0, count), token);
        }
        if (total != expectedSize) throw new InvalidDataException("ファイル容量が不一致");
        if (output is FileStream file) file.Flush(true);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
