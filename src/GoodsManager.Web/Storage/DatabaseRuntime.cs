using Microsoft.Data.Sqlite;

namespace GoodsManager.Web.Storage;

/// <summary>単一インスタンスと停止中CLIの排他、永続領域の設定</summary>
public sealed class DatabaseRuntime : IDisposable
{
    private FileStream? lease;
    public string DirectoryPath { get; }
    public string ConnectionString { get; }

    public DatabaseRuntime(string directory)
    {
        DirectoryPath = Path.GetFullPath(directory);
        ConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(DirectoryPath, "goods-manager.db"),
            ForeignKeys = true,
            DefaultTimeout = 5,
            Pooling = true
        }.ToString();
    }

    /// <summary>アプリ稼働中の別プロセスや復旧CLIによる同時変更を拒否</summary>
    public void AcquireLease() => AcquireLease(false);

    /// <summary>復元中断の処理だけは進行記録が残っていても排他を取得</summary>
    public void AcquireLease(bool allowInterruptedRestore)
    {
        GoodsManager.Web.Backups.BackupPaths.RejectLinks(DirectoryPath);
        Directory.CreateDirectory(DirectoryPath);
        GoodsManager.Web.Backups.BackupPaths.RejectLinks(Path.Combine(DirectoryPath, "instance.lock"));
        lease = new FileStream(Path.Combine(DirectoryPath, "instance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (!allowInterruptedRestore && File.Exists(Path.Combine(DirectoryPath, "restore-state.json")))
        {
            Dispose(); throw new InvalidOperationException("未完了の復元記録が残っているため、停止中にbackup rollbackで確認する必要がある");
        }
    }

    /// <summary>プロセス終了時に排他を解放</summary>
    public void Dispose() => lease?.Dispose();
}
