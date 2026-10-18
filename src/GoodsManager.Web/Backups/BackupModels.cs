using System.Text.RegularExpressions;
using GoodsManager.Web.Storage;
using Microsoft.EntityFrameworkCore;

namespace GoodsManager.Web.Backups;

/// <summary>利用者が変更する設定と、履歴だけでは変更番号を進めない実行管理</summary>
public sealed class BackupSettings
{
    public int Id { get; set; } = 1;
    public bool Enabled { get; set; } = true;
    public string LocalTime { get; set; } = "03:00";
    public string TimeZone { get; set; } = "Asia/Tokyo";
    public long Revision { get; set; } = 1;
    public DateTimeOffset UpdatedAt { get; set; }
    public string? LastAutomaticDate { get; set; }
    public string? LastIntegrityDate { get; set; }
    public string? KeyFingerprint { get; set; }
}

public sealed class BackupRun
{
    public Guid Id { get; set; }
    public string Kind { get; set; } = "manual";
    public string State { get; set; } = "running";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public long? SnapshotDataRevision { get; set; }
    public string? RelativePath { get; set; }
    public string? ManifestHash { get; set; }
    public long? ByteLength { get; set; }
    public string? ErrorCode { get; set; }
    public bool RetentionManaged { get; set; }
}

/// <summary>保全処理同士の排他。業務書込ゲートの前に取得する</summary>
public sealed class BackupGate
{
    public SemaphoreSlim Semaphore { get; } = new(1, 1);
    public object KeyLock { get; } = new();
}

/// <summary>配備環境が設定する処理上限。保存データの総件数とは別に扱う</summary>
public sealed class BackupOptions
{
    public long MaximumArchiveBytes { get; init; } = 64L * 1024 * 1024 * 1024;
    public long MaximumExpandedBytes { get; init; } = 128L * 1024 * 1024 * 1024;
    public int MaximumFiles { get; init; } = 100_000;
    public int MaximumManifestBytes { get; init; } = 16 * 1024 * 1024;
    public bool WorkerEnabled { get; init; } = true;
    public string DirectoryPath { get; }

    public BackupOptions(IConfiguration configuration, DatabaseRuntime runtime)
    {
        configuration.GetSection("Backups").Bind(this);
        DirectoryPath = Path.GetFullPath(configuration["Backups:Directory"] ?? Path.Combine(runtime.DirectoryPath, "backups"));
        if (MaximumArchiveBytes <= 0 || MaximumExpandedBytes <= 0 || MaximumFiles < 2 || MaximumManifestBytes <= 0)
            throw new InvalidOperationException("バックアップ上限が不正");
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(DirectoryPath, runtime.DirectoryPath, comparison) || runtime.DirectoryPath.StartsWith(DirectoryPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, comparison))
            throw new InvalidOperationException("バックアップ保存先はデータ領域そのものと分離する");
        foreach (var reserved in new[] { "images", "keys", "pre-migration", ".backup-work" })
        {
            var path = Path.Combine(runtime.DirectoryPath, reserved);
            if (string.Equals(DirectoryPath, path, comparison) || DirectoryPath.StartsWith(path + Path.DirectorySeparatorChar, comparison)) throw new InvalidOperationException("バックアップ保存先がアプリ管理領域と重複");
        }
    }
}

public static class BackupSchema
{
    /// <summary>設定・実行状態の保存型と状態遷移に必要な範囲を制約</summary>
    public static void Configure(ModelBuilder model)
    {
        model.Entity<BackupSettings>(e =>
        {
            e.ToTable("BackupSettings", t =>
            {
                t.HasCheckConstraint("CK_BackupSettings_Values", "Id=1 AND Revision>=1 AND TimeZone='Asia/Tokyo' AND LocalTime GLOB '[0-2][0-9]:[0-5][0-9]' AND substr(LocalTime,1,2)<'24'");
                t.HasCheckConstraint("CK_BackupSettings_Key", "KeyFingerprint IS NULL OR (length(KeyFingerprint)=64 AND KeyFingerprint NOT GLOB '*[^0-9a-f]*')");
            });
            e.HasKey(x => x.Id); e.Property(x => x.Id).ValueGeneratedNever(); e.Property(x => x.Revision).IsConcurrencyToken();
            e.HasData(new BackupSettings { UpdatedAt = DateTimeOffset.UnixEpoch });
        });
        model.Entity<BackupRun>(e =>
        {
            e.ToTable("BackupRuns", t => t.HasCheckConstraint("CK_BackupRun_Values", "Kind IN ('automatic','manual','preMigration','external') AND State IN ('running','succeeded','failed','deleting','deleted') AND (SnapshotDataRevision IS NULL OR SnapshotDataRevision>=0) AND (ByteLength IS NULL OR ByteLength>0)"));
            e.HasKey(x => x.Id); e.Property(x => x.Id).ValueGeneratedNever();
            e.HasIndex(x => new { x.Kind, x.State, x.CompletedAt }); e.HasIndex(x => x.RelativePath).IsUnique();
        });
    }
}

/// <summary>領域外やリンク先を操作せず、自分で作った一時領域だけを片付ける</summary>
public static class BackupPaths
{
    /// <summary>対象からルートまでのリンクを拒否</summary>
    public static void RejectLinks(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("保全領域内のリンクを拒否");
    }

    /// <summary>指定した親の直下だけを、リンクの検査後に削除</summary>
    public static void DeleteTree(string parent, string path)
    {
        parent = Path.GetFullPath(parent); path = Path.GetFullPath(path);
        if (Path.GetDirectoryName(path) != parent || path == parent) throw new InvalidDataException("削除領域が不正");
        RejectLinks(path);
        if (!Directory.Exists(path)) return;
        foreach (var item in Directory.EnumerateFileSystemEntries(path, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 }))
            RejectLinks(item);
        Directory.Delete(path, true);
    }

    /// <summary>補正せず24時間表記を検証</summary>
    public static bool ValidTime(string value) => Regex.IsMatch(value, "^(?:[01][0-9]|2[0-3]):[0-5][0-9]$", RegexOptions.CultureInvariant);
}
