using GoodsManager.Web.Collections;
using GoodsManager.Web.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoodsManager.Tests;

/// <summary>所属単位の一覧索引と、重複削減後も必要な検索経路が残ることを検証する。</summary>
public sealed class CollectionIndexTests
{
    /// <summary>新規DBの一覧は所属を索引で絞り込み、並べ替え用の一時表を作らない。</summary>
    [TestCase("CreatedAt DESC,ProductId")]
    [TestCase("UpdatedAt DESC,ProductId")]
    [TestCase("NormalizedName,ProductId")]
    public async Task ListsSeekWithinTheirCollection(string order)
    {
        await using var app = new TestApplication();
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.OpenConnectionAsync();
        using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "EXPLAIN QUERY PLAN SELECT ProductId FROM Products WHERE CollectionId=$collection AND DeletedAt IS NULL ORDER BY " + order + " LIMIT 25";
        command.Parameters.Add(new SqliteParameter("$collection", CollectionSchema.DefaultId.ToString("D")));
        var steps = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                steps.Add(reader.GetString(3));
            }
        }

        Assert.That(steps.Any(step => step.StartsWith("SEARCH Products USING ", StringComparison.Ordinal) && step.Contains("INDEX ", StringComparison.Ordinal)
            && step.Contains("CollectionId=?", StringComparison.Ordinal)), Is.True, string.Join(';', steps));
        Assert.That(steps.Any(step => step.Contains("TEMP B-TREE", StringComparison.Ordinal)), Is.False);
        Assert.That(db.Database.HasPendingModelChanges(), Is.False);
    }

    /// <summary>単独索引の削減後も、全所属テーブルに無条件の所属検索経路が残る。</summary>
    [Test]
    public async Task EveryCollectionHasAnAccessPathWithoutRedundantStandaloneIndexes()
    {
        await using var app = new TestApplication();
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var type in CollectionSchema.ScopedTypes)
        {
            var entity = db.Model.FindEntityType(type) ?? throw new InvalidOperationException("所属型のモデルが存在しない");
            var keys = entity.GetKeys().Any(key => key.Properties[0].Name == "CollectionId");
            var indexes = entity.GetIndexes().Where(index => index.GetFilter() is null && index.Properties[0].Name == "CollectionId").ToArray();
            Assert.That(keys || indexes.Length > 0, Is.True, type.Name);
            Assert.That(indexes.Any(index => index.Properties.Count == 1) && (keys || indexes.Any(index => index.Properties.Count > 1)), Is.False, type.Name);
        }
    }
}
