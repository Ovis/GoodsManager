using GoodsManager.Web.Images;
using GoodsManager.Web.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoodsManager.Tests;

/// <summary>所属単位の画像設定に、全体で1件だった旧構造が残らないことを検証する。</summary>
public sealed class ImageSettingsSchemaTests
{
    /// <summary>新規DBの物理列と主キーを確認し、旧ID・旧制約を配布する初期移行から除く。</summary>
    [Test]
    public async Task FreshDatabaseKeysImageSettingsOnlyByCollection()
    {
        await using var app = new TestApplication();
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.OpenConnectionAsync();
        using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "PRAGMA table_info('ImageSettings')";
        var columns = new Dictionary<string, long>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                columns.Add(reader.GetString(1), reader.GetInt64(5));
            }
        }

        Assert.That(columns.ContainsKey("Id"), Is.False);
        Assert.That(columns.Where(column => column.Value > 0).Select(column => column.Key), Is.EqualTo(new[] { "CollectionId" }));
        command.CommandText = "SELECT sql FROM sqlite_master WHERE type='table' AND name='ImageSettings'";
        Assert.That(await command.ExecuteScalarAsync(), Does.Not.Contain("CK_ImageSettings_Singleton"));
        Assert.That(await db.Set<ImageSettings>().CountAsync(), Is.EqualTo(1));
        Assert.That(db.Database.HasPendingModelChanges(), Is.False);
    }
}
