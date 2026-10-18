using System.IO.Compression;
using GoodsManager.Web.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Reflection;

namespace GoodsManager.Tests;

/// <summary>実ファイルSQLiteの制約、型変換、取消、移行保護を検証</summary>
public sealed class SqliteTests
{
    private string directory = null!;
    private string connectionString = null!;

    /// <summary>試験ごとのDBを作成</summary>
    [SetUp]
    public void SetUp()
    {
        directory = Path.Combine(Path.GetTempPath(), "GoodsManager.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        connectionString = new DatabaseRuntime(directory).ConnectionString;
    }

    /// <summary>開いたファイルを閉じて試験データを削除</summary>
    [TearDown]
    public void TearDown()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(directory, true);
    }

    /// <summary>共通変換を適用した試験専用スキーマを作る。本番へ将来用テーブルを追加しない</summary>
    private async Task<ContractContext> CreateContractAsync()
    {
        var db = new ContractContext(new DbContextOptionsBuilder<ContractContext>().UseSqlite(connectionString).Options);
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    /// <summary>パラメーター付き直接SQLで元の入力形式と保存型を区別</summary>
    private async Task<object?> ScalarAsync(string sql, object? value = null)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@value", value ?? DBNull.Value);
        return await command.ExecuteScalarAsync();
    }

    /// <summary>SQ01/SQ02/SQ04: 通常テーブル、外部キー、整数親和性と不正型</summary>
    [Test]
    public async Task NormalTablesEnforceStorageAndForeignKeys()
    {
        await using var db = await CreateContractAsync();
        var row = new ContractRow();
        db.Rows.Add(row);
        await db.SaveChangesAsync();
        Assert.That(await ScalarAsync("SELECT strict FROM pragma_table_list WHERE name='Rows'"), Is.EqualTo(0L));
        Assert.That(await ScalarAsync("PRAGMA foreign_keys"), Is.EqualTo(1L));
        TestContext.Out.WriteLine($"SQLite {await ScalarAsync("SELECT sqlite_version()")}");
        Assert.That(await ScalarAsync("SELECT count(*) FROM pragma_compile_options"), Is.GreaterThan(0));
        foreach (var invalid in new object?[] { 0, -1, 2147483648L, 1.5, "abc", new byte[] { 1 }, null })
        {
            Assert.ThrowsAsync<SqliteException>(async () => await ScalarAsync("UPDATE Rows SET Quantity=@value", invalid));
        }

        foreach (var valid in new object[] { 1, int.MaxValue, "2" })
        {
            await ScalarAsync("UPDATE Rows SET Quantity=@value", valid);
            Assert.That(await ScalarAsync("SELECT typeof(Quantity) FROM Rows"), Is.EqualTo("integer"));
        }

        foreach (var invalid in new object?[] { 2, -1, null, 1.5 })
        {
            Assert.ThrowsAsync<SqliteException>(async () => await ScalarAsync("UPDATE Rows SET Flag=@value", invalid));
        }

        Assert.ThrowsAsync<SqliteException>(async () => await ScalarAsync("UPDATE Rows SET ParentId=@value", Guid.NewGuid().ToString("D")));
        Assert.ThrowsAsync<SqliteException>(async () => await ScalarAsync("UPDATE Rows SET Id=NULL"));
    }

    /// <summary>SQ05/SQ06/SQ08: UUID、日時、NULL、BLOBとJSONの往復</summary>
    [Test]
    public async Task ConvertersPreserveValuesAndSqlComparisons()
    {
        await using var db = await CreateContractAsync();
        var instants = new[] { StorageValues.MinimumTimestamp, -1L, 0L, 1L, StorageValues.MaximumTimestamp };
        foreach (var instant in instants)
        {
            db.Rows.Add(new ContractRow { Instant = StorageValues.FromMilliseconds(instant) });
        }

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var epoch = DateTimeOffset.UnixEpoch;
        var query = db.Rows.Where(x => x.Instant >= epoch).OrderBy(x => x.Instant);
        TestContext.Out.WriteLine(query.ToQueryString());
        Assert.That(query.ToQueryString(), Does.Contain("ORDER BY").And.Contain("WHERE"));
        Assert.That(query.ToQueryString(), Does.Not.Contain("1970"));
        Assert.That((await query.ToListAsync()).Select(x => StorageValues.ToMilliseconds(x.Instant)), Is.EqualTo(new[] { 0L, 1L, StorageValues.MaximumTimestamp }));
        var rows = await db.Rows.ToListAsync();
        Assert.That(rows.All(x => x.ParentId is null && x.OptionalInstant is null && x.Bytes.SequenceEqual(new byte[] { 0, 255, 0 })), Is.True);
        Assert.That(await ScalarAsync("SELECT typeof(Id)||','||typeof(Instant)||','||typeof(Bytes)||','||typeof(Json) FROM Rows LIMIT 1"), Is.EqualTo("text,integer,blob,text"));
        var id = rows[0].Id;
        Assert.That(await db.Rows.AnyAsync(x => x.Id == id), Is.True);

        foreach (var invalid in new object[] { id.ToString("D").ToUpperInvariant(), id.ToString("N"), "g0000000-0000-0000-0000-000000000000", "0000000--0000-0000-0000-000000000000", new byte[36] })
        {
            Assert.ThrowsAsync<SqliteException>(async () => await ScalarAsync("UPDATE Rows SET Id=@value WHERE Id=(SELECT Id FROM Rows LIMIT 1)", invalid));
        }

        foreach (var invalid in new object[] { StorageValues.MinimumTimestamp - 1, StorageValues.MaximumTimestamp + 1, "bad", 0.5 })
        {
            Assert.ThrowsAsync<SqliteException>(async () => await ScalarAsync("UPDATE Rows SET Instant=@value", invalid));
        }

        Assert.ThrowsAsync<SqliteException>(async () => await ScalarAsync("UPDATE Rows SET Bytes=@value", "AP8A"));
        Assert.ThrowsAsync<SqliteException>(async () => await ScalarAsync("UPDATE Rows SET Json=@value", "{"));
        await ScalarAsync("UPDATE Rows SET Json='null', OptionalInstant=NULL");
        Assert.That(await ScalarAsync("SELECT json_type(Json) FROM Rows LIMIT 1"), Is.EqualTo("null"));
        Assert.That(StorageValues.ToMilliseconds(epoch.AddTicks(-1)), Is.EqualTo(-1));
        Assert.That(StorageValues.ToMilliseconds(epoch.AddTicks(1)), Is.EqualTo(0));
        Assert.That(StorageValues.ToMilliseconds(epoch.ToOffset(TimeSpan.FromHours(9))), Is.Zero);
    }

    /// <summary>SQ07: コードポイント数とNUL、不正なUTF-16の拒否</summary>
    [TestCase("あ😀", true)]
    [TestCase("e\u0301", true)]
    [TestCase("👨‍👩‍👧‍👦", false)]
    [TestCase("あ😀い", false)]
    [TestCase("\0あ", false)]
    [TestCase("あ\0い", false)]
    [TestCase("あ\0", false)]
    public async Task TextUsesCodePoints(string text, bool valid)
    {
        await using var db = await CreateContractAsync();
        db.Rows.Add(new ContractRow());
        await db.SaveChangesAsync();
        Assert.That(StorageValues.IsValidText(text, 1, 2), Is.EqualTo(valid));
        if (valid)
        {
            await ScalarAsync("UPDATE Rows SET Name=@value", text);
        }
        else
        {
            Assert.ThrowsAsync<SqliteException>(async () => await ScalarAsync("UPDATE Rows SET Name=@value", text));
        }

        Assert.That(StorageValues.IsValidText("\ud800", 1, 2), Is.False);
    }

    /// <summary>SQ03/SQ04/SQ11: 整数の上限と、保存済み変更を含む全体取消</summary>
    [Test]
    public async Task OverflowAndExceptionRollbackAllChanges()
    {
        await using var db = await CreateContractAsync();
        db.Rows.AddRange(new ContractRow { Revision = long.MaxValue }, new ContractRow { Revision = 1 });
        await db.SaveChangesAsync();
        Assert.ThrowsAsync<SqliteException>(async () => await ScalarAsync("SELECT SUM(Revision) FROM Rows"));
        Assert.ThrowsAsync<SqliteException>(async () => await ScalarAsync("UPDATE Rows SET Revision=Revision+1 WHERE Revision=9223372036854775807"));
        Assert.That(StorageValues.RequireSafeTotal(StorageValues.MaximumSafeInteger), Is.EqualTo(StorageValues.MaximumSafeInteger));
        Assert.Throws<OverflowException>(() => StorageValues.RequireSafeTotal(StorageValues.MaximumSafeInteger + 1));
        Assert.Throws<InvalidDataException>(() => StorageValues.ParseBoolean(2));

        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            db.Rows.Add(new ContractRow());
            await db.SaveChangesAsync();
            Assert.That(await db.Rows.CountAsync(), Is.EqualTo(3));
            await transaction.RollbackAsync();
        }

        db.ChangeTracker.Clear();
        Assert.That(await db.Rows.CountAsync(), Is.EqualTo(2));
    }

    /// <summary>SQ10/SQ12: 実マイグレーション、再開、更新失敗の退避と部分変更の取消</summary>
    [Test]
    public async Task MigrationFailureLeavesBackupAndDoesNotPartiallyCommit()
    {
        await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connectionString).Options))
        {
            await DatabaseMaintenance.MigrateAsync(db, directory);
            await db.Database.ExecuteSqlRawAsync("UPDATE SystemState SET DataRevision=77");
        }

        Directory.CreateDirectory(Path.Combine(directory, "keys"));
        await File.WriteAllTextAsync(Path.Combine(directory, "keys", "test-key.xml"), "preserved-key");
        await using var broken = new FailingContext(new DbContextOptionsBuilder<FailingContext>().UseSqlite(connectionString).ReplaceService<IMigrationsAssembly, FailingMigrationsAssembly>().Options);
        Assert.ThrowsAsync<SqliteException>(async () => await DatabaseMaintenance.MigrateAsync(broken, directory));
        Assert.That(await ScalarAsync("SELECT DataRevision FROM SystemState"), Is.EqualTo(77L));
        var backup = Directory.GetFiles(Path.Combine(directory, "pre-migration"), "*.zip").Single();
        using var zip = ZipFile.OpenRead(backup);
        Assert.That(zip.GetEntry("keys/test-key.xml"), Is.Not.Null);
        var restored = Path.Combine(directory, "restored.db");
        zip.GetEntry("goods-manager.db")!.ExtractToFile(restored);
        using var connection = new SqliteConnection($"Data Source={restored};Pooling=False");
        connection.Open();
        await DatabaseMaintenance.VerifyAsync(connection);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT DataRevision FROM SystemState";
        Assert.That(command.ExecuteScalar(), Is.EqualTo(77L));
    }

    /// <summary>SQ12: アプリと停止中CLIが同時にDBを管理しない</summary>
    [Test]
    public void ProcessLeaseRejectsConcurrentOwner()
    {
        using var first = new DatabaseRuntime(directory);
        first.AcquireLease();
        using var second = new DatabaseRuntime(directory);
        Assert.Throws<IOException>(second.AcquireLease);
        first.Dispose();
        Assert.DoesNotThrow(second.AcquireLease);
    }

    /// <summary>SQ12: 別接続の書込競合が一部確定や誤った成功にならない</summary>
    [Test]
    public async Task SeparateConnectionsRespectWriteLockAndRollback()
    {
        await using var db = await CreateContractAsync();
        db.Rows.Add(new ContractRow());
        await db.SaveChangesAsync();
        await using var first = new SqliteConnection(connectionString);
        await using var second = new SqliteConnection(connectionString);
        await first.OpenAsync();
        await second.OpenAsync();
        await using var transaction = first.BeginTransaction();
        using var update = first.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = "UPDATE Rows SET Quantity=2";
        await update.ExecuteNonQueryAsync();
        using var competing = second.CreateCommand();
        competing.CommandTimeout = 1;
        competing.CommandText = "UPDATE Rows SET Quantity=3";
        var failure = Assert.ThrowsAsync<SqliteException>(async () => await competing.ExecuteNonQueryAsync());
        Assert.That(failure!.SqliteErrorCode, Is.EqualTo(5));
        await transaction.RollbackAsync();
        Assert.That(await ScalarAsync("SELECT Quantity FROM Rows"), Is.EqualTo(1));
    }
}

/// <summary>保存型を検証する試験専用データ</summary>
public sealed class ContractRow
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? ParentId { get; set; }
    public DateTimeOffset Instant { get; set; } = DateTimeOffset.UnixEpoch;
    public DateTimeOffset? OptionalInstant { get; set; }
    public int Quantity { get; set; } = 1;
    public long Revision { get; set; } = 1;
    public bool Flag { get; set; }
    public byte[] Bytes { get; set; } = [0, 255, 0];
    public string Name { get; set; } = "あ";
    public string Json { get; set; } = "{}";
}

public sealed class ContractContext(DbContextOptions<ContractContext> options) : DbContext(options)
{
    public DbSet<ContractRow> Rows => Set<ContractRow>();

    /// <summary>型契約を本番と同じ共通処理で適用</summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ContractRow>().HasOne<ContractRow>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ContractRow>().ToTable("Rows", table =>
        {
            table.HasCheckConstraint("CK_Quantity", "Quantity BETWEEN 1 AND 2147483647");
            table.HasCheckConstraint("CK_Revision", "Revision >= 1");
            table.HasCheckConstraint("CK_Name", "length(Name) BETWEEN 1 AND 2");
            table.HasCheckConstraint("CK_Json", "json_valid(Json) = 1");
        });
        StorageValues.ApplyConventions(modelBuilder);
    }
}

public sealed class FailingContext(DbContextOptions<FailingContext> options) : DbContext(options);

/// <summary>本番の移行履歴を保ったまま、その次に失敗する試験用移行だけを追加</summary>
public sealed class FailingMigrationsAssembly : IMigrationsAssembly
{
    public Assembly Assembly => typeof(AppDbContext).Assembly;
    public ModelSnapshot? ModelSnapshot => null;
    public IReadOnlyDictionary<string, TypeInfo> Migrations { get; } = typeof(AppDbContext).Assembly.GetTypes()
        .Append(typeof(FailingMigration))
        .Where(type => typeof(Migration).IsAssignableFrom(type) && type.GetCustomAttribute<MigrationAttribute>() is not null)
        .OrderBy(type => type.GetCustomAttribute<MigrationAttribute>()!.Id, StringComparer.Ordinal)
        .ToDictionary(type => type.GetCustomAttribute<MigrationAttribute>()!.Id, type => type.GetTypeInfo());

    /// <summary>EFの移行選択に必要なIDを返す</summary>
    public string? FindMigrationId(string nameOrId) => Migrations.Keys.SingleOrDefault(id =>
        id.Equals(nameOrId, StringComparison.OrdinalIgnoreCase) || id.EndsWith("_" + nameOrId, StringComparison.OrdinalIgnoreCase));

    /// <summary>公開インターフェースを通して実際の移行を生成</summary>
    public Migration CreateMigration(TypeInfo migrationClass, string activeProvider)
    {
        var migration = (Migration)Activator.CreateInstance(migrationClass.AsType())!;
        migration.ActiveProvider = activeProvider;
        return migration;
    }
}

[DbContext(typeof(FailingContext))]
[Migration("20990101000000_Failing")]
public sealed class FailingMigration : Migration
{
    /// <summary>途中で失敗する移行を再現</summary>
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("UPDATE SystemState SET DataRevision=999");
        migrationBuilder.Sql("INSERT INTO MissingTable VALUES (1)");
    }

    /// <summary>失敗試験では巻き戻し用の移行を実行しない</summary>
    protected override void Down(MigrationBuilder migrationBuilder) { }
}
