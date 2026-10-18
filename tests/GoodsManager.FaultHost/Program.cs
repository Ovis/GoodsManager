using GoodsManager.Web.Backups;
using GoodsManager.Web.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace GoodsManager.FaultHost;

/// <summary>親試験だけが起動し、実プロセス終了でfinallyを実行させない試験専用ホスト</summary>
public static class Program
{
    /// <summary>復元境界または未コミットDB更新で親の終了操作を待機</summary>
    public static async Task Main(string[] args)
    {
        using var runtime = new DatabaseRuntime(args[1]); runtime.AcquireLease();
        if (args[0] == "restore")
        {
            var options = new BackupOptions(new ConfigurationBuilder().Build(), runtime);
            await new BackupRestore(runtime, options).RestoreAsync(args[2], default, point => { if (point == args[3]) Pause(); });
        }
        else if (args[0] == "transaction")
        {
            await using var connection = new SqliteConnection(runtime.ConnectionString); await connection.OpenAsync();
            await using var transaction = connection.BeginTransaction();
            await using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "UPDATE InventoryLots SET Quantity=999; UPDATE SystemState SET DataRevision=DataRevision+1";
            await command.ExecuteNonQueryAsync(); Pause();
        }
        else throw new ArgumentException("未定義の試験操作");
    }

    /// <summary>親が境界への到達を確認した後に子プロセスを強制終了</summary>
    private static void Pause() { Console.WriteLine("BOUNDARY_REACHED"); Console.Out.Flush(); Thread.Sleep(Timeout.Infinite); }
}
