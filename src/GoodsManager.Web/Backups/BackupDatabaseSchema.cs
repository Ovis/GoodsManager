using GoodsManager.Web.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GoodsManager.Web.Backups;

/// <summary>移行履歴だけで互換と扱わず、その版が要求する実際の列構成を照合</summary>
public static class BackupDatabaseSchema
{
    /// <summary>アプリが持つ該当マイグレーションの列名・保存型を実DBと比較</summary>
    public static async Task VerifyAsync(AppDbContext context, SqliteConnection database, string schemaVersion, CancellationToken token)
    {
        var migrations = context.GetService<IMigrationsAssembly>();
        if (!migrations.Migrations.TryGetValue(schemaVersion, out var type)) throw new InvalidDataException("対応しないスキーマ");
        var target = migrations.CreateMigration(type, context.Database.ProviderName!).TargetModel;
        foreach (var group in target.GetEntityTypes().Where(x => !x.IsOwned()).GroupBy(x => x.GetTableName()))
        {
            if (group.Key is null) continue;
            using var command = database.CreateCommand(); command.CommandText = "SELECT name,type FROM pragma_table_info($table)"; command.Parameters.AddWithValue("$table", group.Key);
            var columns = new Dictionary<string, string>(StringComparer.Ordinal);
            await using (var reader = await command.ExecuteReaderAsync(token)) while (await reader.ReadAsync(token)) columns.Add(reader.GetString(0), reader.GetString(1));
            if (columns.Count == 0) throw new InvalidDataException("移行履歴に対応するテーブルが存在しない");
            foreach (var entity in group)
            {
                var table = StoreObjectIdentifier.Table(group.Key, entity.GetSchema());
                foreach (var property in entity.GetProperties())
                {
                    var name = property.GetColumnName(table);
                    if (name is null) continue;
                    if (!columns.TryGetValue(name, out var actual) || property.GetColumnType() is string expected && !string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("移行履歴と実際の列構成が不一致");
                }
            }
        }
    }
}
