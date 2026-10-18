using System.Net;
using GoodsManager.Web.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace GoodsManager.Tests;

/// <summary>移行履歴の不整合と異常終了後の起動判定を検証</summary>
public sealed class MigrationReviewTests
{
    /// <summary>更新済みDBでも、未知の移行履歴や残留ロックを準備完了と判定しない</summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task StartupRejectsUnknownHistoryOrUnreleasedMigrationLock(bool lockRemains)
    {
        string directory;
        await using (var original = new TestApplication { PreserveData = true })
        {
            directory = original.DataDirectory;
            using var client = original.CreateClient();
            Assert.That((await client.GetAsync("/health/ready")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        await ExecuteAsync(directory, lockRemains
            ? "INSERT INTO __EFMigrationsLock(Id,Timestamp) VALUES(1,'2026-10-18')"
            : "INSERT INTO __EFMigrationsHistory(MigrationId,ProductVersion) VALUES('20990101000000_Unknown','99.0.0')");

        await using var restarted = new TestApplication(directory);
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            using var client = restarted.CreateClient();
            await client.GetAsync("/health/ready");
        });
        Assert.That(await ExecuteAsync(directory, lockRemains ? "SELECT COUNT(*) FROM __EFMigrationsLock WHERE Id=1" : "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId='20990101000000_Unknown'"), Is.EqualTo(1));
    }

    /// <summary>稼働中に移行履歴が変わった場合も、匿名の準備状態は503</summary>
    [Test]
    public async Task ReadinessRejectsUnknownAppliedMigration()
    {
        await using var app = new TestApplication();
        using var client = app.CreateClient();
        Assert.That((await client.GetAsync("/health/ready")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        await using var scope = app.Services.CreateAsyncScope();
        var runtime = scope.ServiceProvider.GetRequiredService<DatabaseRuntime>();
        await using var connection = new SqliteConnection(runtime.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO __EFMigrationsHistory(MigrationId,ProductVersion) VALUES('20990101000000_Unknown','99.0.0')";
        await command.ExecuteNonQueryAsync();
        var response = await client.GetAsync("/health/ready");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Not.Contain("Unknown").And.Not.Contain(app.DataDirectory));
    }

    /// <summary>停止中の一時DBを変更・照会し、再起動前に接続を解放</summary>
    private static async Task<object?> ExecuteAsync(string directory, string sql)
    {
        await using var connection = new SqliteConnection(new DatabaseRuntime(directory).ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }
}
