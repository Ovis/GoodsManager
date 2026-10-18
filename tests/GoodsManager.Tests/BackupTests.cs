using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using GoodsManager.Web.Backups;
using GoodsManager.Web.Images;
using GoodsManager.Web.Masters;
using GoodsManager.Web.Products;
using GoodsManager.Web.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using static GoodsManager.Tests.AuthenticationTests;

namespace GoodsManager.Tests;

/// <summary>実ZIP・実SQLiteで保全範囲、失敗時の世代保持、復元切替を検証</summary>
public sealed class BackupTests
{
    private TestApplication app = null!;
    private HttpClient owner = null!;
    private Guid productId;
    private string imagePath = "";

    [SetUp]
    public async Task Setup()
    {
        app = new TestApplication(); owner = Client(app); await SetupAsync(owner);
        await using var scope = app.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        productId = Guid.NewGuid(); var now = app.Clock.GetUtcNow();
        db.Add(new Product { Id = productId, Name = "バックアップ対象", NormalizedName = "バックアップ対象", CategoryId = await db.Set<Category>().Select(x => x.Id).FirstAsync(), CreatedAt = now, UpdatedAt = now });
        db.Add(new InventoryLot { Id = Guid.NewGuid(), ProductId = productId, Quantity = 2, Memo = "ロットのメモ", CreatedAt = now, UpdatedAt = now });
        var item = new InventoryItem { Id = Guid.NewGuid(), ProductId = productId, Memo = "個体のメモ", CreatedAt = now, UpdatedAt = now }; db.Add(item);
        var bytes = new byte[] { 7, 10, 20, 100 }; var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var file = new ImageFile { Id = Guid.NewGuid(), Sha256 = hash, RelativePath = ImageFiles.ObjectPath(hash), ByteLength = bytes.Length, MediaType = "image/webp", Width = 1, Height = 1, CreatedAt = now, UpdatedAt = now };
        var policy = ImagePolicy.From(await db.Set<ImageSettings>().SingleAsync());
        var asset = new ImageAsset { Id = Guid.NewGuid(), SourceSha256 = hash, PolicyHash = policy.Hash, PolicyJson = policy.Json, DisplayFileId = file.Id, ThumbnailFileId = file.Id, CreatedAt = now };
        db.Add(file); db.Add(asset);
        db.Add(new ProductImage { Id = Guid.NewGuid(), ProductId = productId, ImageAssetId = asset.Id, IsPrimary = true, CreatedAt = now, UpdatedAt = now });
        db.Add(new InventoryItemImage { Id = Guid.NewGuid(), InventoryItemId = item.Id, ImageAssetId = asset.Id, CreatedAt = now, UpdatedAt = now });
        // 未使用アップロードは稼働DBに残し、コピー側からだけ除去する。
        var unusedBytes = new byte[] { 9, 8 }; var unusedHash = Convert.ToHexStringLower(SHA256.HashData(unusedBytes));
        var unusedFile = new ImageFile { Id = Guid.NewGuid(), Sha256 = unusedHash, RelativePath = ImageFiles.ObjectPath(unusedHash), ByteLength = 2, MediaType = "image/webp", Width = 1, Height = 1, CreatedAt = now, UpdatedAt = now };
        var unusedAsset = new ImageAsset { Id = Guid.NewGuid(), SourceSha256 = unusedHash, PolicyHash = policy.Hash, PolicyJson = policy.Json, DisplayFileId = unusedFile.Id, ThumbnailFileId = unusedFile.Id, CreatedAt = now };
        db.Add(unusedFile); db.Add(unusedAsset);
        db.Add(new TemporaryUpload { Id = Guid.NewGuid(), UserId = (await db.Users.SingleAsync()).Id, ClientUploadKey = Guid.NewGuid(), State = "ready", SourceSha256 = unusedHash, PolicyHash = policy.Hash, PolicyJson = policy.Json, ByteLength = 2, ImageAssetId = unusedAsset.Id, ExpiresAt = now.AddDays(1), ReplayUntil = now.AddDays(7), CreatedAt = now, UpdatedAt = now });

        await db.SaveChangesAsync();
        var images = scope.ServiceProvider.GetRequiredService<IImageFiles>(); imagePath = images.Resolve(file.RelativePath); Directory.CreateDirectory(Path.GetDirectoryName(imagePath)!); await File.WriteAllBytesAsync(imagePath, bytes);
        var unusedPath = images.Resolve(unusedFile.RelativePath); Directory.CreateDirectory(Path.GetDirectoryName(unusedPath)!); await File.WriteAllBytesAsync(unusedPath, unusedBytes);
    }
    [TearDown]
    public async Task Cleanup() { owner.Dispose(); await app.DisposeAsync(); }

    /// <summary>202の受付再送は同じ実行ID。コピーからだけセッション・再送記録を除去</summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task ManualReplayAndSnapshotPreserveFormalDataWithoutTransientState(bool trashed)
    {
        if (trashed) { await using var scope = app.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); (await db.Set<Product>().SingleAsync()).DeletedAt = app.Clock.GetUtcNow(); await db.SaveChangesAsync(); }
        var key = Guid.NewGuid(); var first = await Write("backups", new { kind = "manual" }, key);
        Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.Accepted), await first.Content.ReadAsStringAsync());
        var id = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var repeated = await Write("backups", new { kind = "manual" }, key);
        Assert.That((await repeated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(), Is.EqualTo(id));
        Assert.That((await Write("backups", new { kind = "automatic" }, key)).StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        await Run(id); var path = await PathFor(id);
        using var zip = ZipFile.OpenRead(path);
        Assert.That(zip.Entries.Count(x => x.FullName.StartsWith("images/")), Is.EqualTo(1));
        var stage = Path.Combine(app.DataDirectory, ".test-verify");
        try
        {
            Directory.CreateDirectory(stage); await using var input = File.OpenRead(path);
            var manifest = await BackupArchive.VerifyAsync(input, Options(), stage, default);
            Assert.That(manifest.RunId, Is.EqualTo(id));
            await using var copy = BackupArchive.OpenDatabase(stage, true); await copy.OpenAsync();
            Assert.That(Convert.ToInt64(await BackupArchive.ScalarAsync(copy, "SELECT count(*) FROM AuthTickets", default)), Is.Zero);
            Assert.That(Convert.ToInt64(await BackupArchive.ScalarAsync(copy, "SELECT count(*) FROM IdempotencyRecords", default)), Is.Zero);
            Assert.That(Convert.ToInt64(await BackupArchive.ScalarAsync(copy, "SELECT count(*) FROM InventoryItemImages", default)), Is.EqualTo(1));
            Assert.That(Convert.ToInt64(await BackupArchive.ScalarAsync(copy, "SELECT count(*) FROM ProductImages", default)), Is.EqualTo(1));
            Assert.That(Convert.ToInt64(await BackupArchive.ScalarAsync(copy, "SELECT count(*) FROM TemporaryUploads", default)), Is.Zero);
            Assert.That(Convert.ToInt64(await BackupArchive.ScalarAsync(copy, "SELECT count(*) FROM ImageAssets", default)), Is.EqualTo(1));
            Assert.That(Convert.ToInt64(await BackupArchive.ScalarAsync(copy, "SELECT sum(Quantity) FROM InventoryLots", default)), Is.EqualTo(2));
        }
        finally { BackupPaths.DeleteTree(app.DataDirectory, stage); }
        var revision = await Revision(); var second = await Create(); await Run(second); Assert.That(await Revision(), Is.EqualTo(revision));
        Assert.That((await owner.GetAsync($"/api/v1/backups/{id}/download")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await owner.GetAsync("/api/v1/auth/session")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    /// <summary>設定の競合・型・固定項目を拒否し、変更なし保存や履歴で変更番号を増やさない</summary>
    [Test]
    public async Task SettingsUseVersionAndDoNotChangeDataForNoOp()
    {
        var settings = await owner.GetFromJsonAsync<JsonElement>("/api/v1/settings/backups"); var etag = settings.GetProperty("etag").GetString(); var revision = await Revision();
        Assert.That((await Write("settings/backups", new { enabled = true, localTime = "03:00" }, Guid.NewGuid(), HttpMethod.Put, etag)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await Revision(), Is.EqualTo(revision));
        Assert.That((await Write("settings/backups", new { enabled = false, localTime = "24:00" }, Guid.NewGuid(), HttpMethod.Put, etag)).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That((await Write("settings/backups", new { enabled = false, localTime = "03:01", destinationDisplay = "/other" }, Guid.NewGuid(), HttpMethod.Put, etag)).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That((await Write("settings/backups", new { enabled = false, localTime = "03:01" }, Guid.NewGuid(), HttpMethod.Put, etag)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await Revision(), Is.EqualTo(revision + 1));
        Assert.That((await Write("settings/backups", new { enabled = true, localTime = "03:00" }, Guid.NewGuid(), HttpMethod.Put, etag)).StatusCode, Is.EqualTo(HttpStatusCode.PreconditionFailed));
    }

    /// <summary>変更なしは省略し、成功3世代だけ整理。失敗や履歴更新で旧成功を失わない</summary>
    [Test]
    public async Task AutomaticRetentionAndFailedSnapshotKeepPreviousSuccesses()
    {
        app.Clock.Advance(new DateTimeOffset(2026, 10, 19, 4, 0, 0, TimeSpan.FromHours(9)) - app.Clock.GetUtcNow());
        await Tick(); Assert.That(await Count("automatic", "succeeded"), Is.EqualTo(1));
        app.Clock.Advance(TimeSpan.FromDays(1)); await Tick(); Assert.That(await Count("automatic", "succeeded"), Is.EqualTo(1));
        var manual = await Create(); await Run(manual);
        for (var i = 0; i < 4; ++i)
        {
            app.Clock.Advance(TimeSpan.FromDays(1)); await Change(); await Tick();
        }
        Assert.That(await Count("automatic", "succeeded"), Is.EqualTo(3));
        Assert.That(File.Exists(await PathFor(manual)), Is.True);
        await using (var exclusive = app.Services.CreateAsyncScope())
        {
            var gate = exclusive.ServiceProvider.GetRequiredService<GoodsManager.Web.Auth.AuthWriteGate>();
            await gate.Semaphore.WaitAsync(); try { File.Delete(imagePath); } finally { gate.Semaphore.Release(); }
        }
        var failed = await Create(); await Run(failed);
        Assert.That(await State(failed), Is.EqualTo("failed")); Assert.That(await Count("automatic", "succeeded"), Is.EqualTo(3));
        await using var scope = app.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.That(await db.Set<SystemAlert>().AnyAsync(x => x.Code == "BACKUP_FAILED" && x.ResolvedAt == null), Is.True);
    }

    /// <summary>外部配置した自動ZIPは、同じ保存先でも自動保持削除へ巻き込まない</summary>
    [Test]
    public async Task ExternalArchiveIsNeverAutomaticallyPruned()
    {
        var id = await Create(); await Run(id); var original = await PathFor(id); var external = Guid.NewGuid();
        var path = Path.Combine(Path.GetDirectoryName(original)!, external.ToString("N") + ".zip");
        Rewrite(original, path, manifest => manifest with { RunId = external, Kind = "automatic" });
        await using var scope = app.Services.CreateAsyncScope(); await scope.ServiceProvider.GetRequiredService<BackupService>().InitializeAsync(default);
        var run = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<BackupRun>().SingleAsync(x => x.Id == external);
        Assert.That(run.Kind, Is.EqualTo("external")); Assert.That(run.RetentionManaged, Is.False); Assert.That(File.Exists(path), Is.True);
    }

    /// <summary>危険なZIPは本体へ展開せず拒否する</summary>
    [TestCase("traversal")]
    [TestCase("duplicate")]
    [TestCase("symlink")]
    [TestCase("hash")]
    [TestCase("manifest")]
    [TestCase("archiveLimit")]
    [TestCase("expandedLimit")]
    [TestCase("countLimit")]
    public async Task RejectsDamagedOrUnboundedArchives(string kind)
    {
        var id = await Create(); await Run(id); var source = await PathFor(id); var path = Path.Combine(app.DataDirectory, "invalid.zip");
        Rewrite(source, path, manifest => kind == "hash" ? manifest with { Files = manifest.Files.Select((x, i) => i == 0 ? x with { Sha256 = new string('0', 64) } : x).ToArray() } : kind == "manifest" ? manifest with { FormatVersion = 99 } : manifest, kind);
        var options = Options();
        if (kind == "archiveLimit") options = new BackupOptions(new ConfigurationBuilder().Build(), new DatabaseRuntime(app.DataDirectory)) { MaximumArchiveBytes = 1 };
        if (kind == "expandedLimit") options = new BackupOptions(new ConfigurationBuilder().Build(), new DatabaseRuntime(app.DataDirectory)) { MaximumExpandedBytes = 1 };
        if (kind == "countLimit") options = new BackupOptions(new ConfigurationBuilder().Build(), new DatabaseRuntime(app.DataDirectory)) { MaximumFiles = 2 };
        await using var input = File.OpenRead(path);
        Assert.ThrowsAsync<InvalidDataException>(async () => await BackupArchive.VerifyAsync(input, options, null, default));
        Assert.That(File.Exists(Path.Combine(app.DataDirectory, "escaped")), Is.False);
    }

    /// <summary>復元後は正式画像と所持を戻し、旧クッキーと再送成功記録を使えなくする</summary>
    [Test]
    public async Task RestorePreservesOwnershipAndKeysButInvalidatesOldSessions()
    {
        var id = await Create(); await Run(id); var path = await PathFor(id);
        var login = await SendAsync(owner, "auth/password", new { loginId = "owner", password = Password, rememberMe = true });
        var cookie = login.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("__Host-GoodsManager=")).Split(';')[0];
        await Change(); app.PreserveData = true; await app.DisposeAsync();
        using (var runtime = new DatabaseRuntime(app.DataDirectory))
        {
            runtime.AcquireLease(); await new BackupRestore(runtime, Options()).RestoreAsync(path, default);
            await using var db = BackupArchive.OpenDatabase(app.DataDirectory, true); await db.OpenAsync();
            Assert.That(Convert.ToInt64(await BackupArchive.ScalarAsync(db, "SELECT count(*) FROM AuthTickets", default)), Is.Zero);
            Assert.That(Convert.ToInt64(await BackupArchive.ScalarAsync(db, "SELECT count(*) FROM IdempotencyRecords", default)), Is.Zero);
            Assert.That(await BackupArchive.ScalarAsync(db, "SELECT Name FROM Products", default), Is.EqualTo("バックアップ対象"));
        }
        app = new TestApplication(app.DataDirectory); owner.Dispose(); owner = Client(app); owner.DefaultRequestHeaders.Add("Cookie", cookie);
        Assert.That((await owner.GetAsync("/api/v1/auth/session")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        owner.DefaultRequestHeaders.Remove("Cookie"); Assert.That((await SendAsync(owner, "auth/password", new { loginId = "owner", password = Password, rememberMe = false })).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await owner.GetFromJsonAsync<JsonElement>($"/api/v1/products/{productId}")).GetProperty("totalQuantity").GetInt64(), Is.EqualTo(3));
        Assert.That(await File.ReadAllBytesAsync(imagePath), Is.EqualTo(new byte[] { 7, 10, 20, 100 }));
    }

    /// <summary>DB・画像・鍵それぞれの改名後に失敗しても、旧一式へ戻す</summary>
    [TestCase("old:goods-manager.db")]
    [TestCase("new:images")]
    [TestCase("new:keys")]
    public async Task RestoreFailureRollsBackAllMembers(string stop)
    {
        var id = await Create(); await Run(id); var path = await PathFor(id);
        await Change(); app.PreserveData = true; await app.DisposeAsync();
        using var runtime = new DatabaseRuntime(app.DataDirectory); runtime.AcquireLease();
        Assert.ThrowsAsync<IOException>(async () => await new BackupRestore(runtime, Options()).RestoreAsync(path, default, point => { if (point == stop) throw new IOException("試験中断"); }));
        await using var db = BackupArchive.OpenDatabase(app.DataDirectory, true); await db.OpenAsync();
        Assert.That(await BackupArchive.ScalarAsync(db, "SELECT Name FROM Products", default), Is.EqualTo("変更後"));
        Assert.That(File.Exists(Path.Combine(app.DataDirectory, "restore-state.json")), Is.False);
        runtime.Dispose(); app = new TestApplication(app.DataDirectory);
    }

    /// <summary>プロセス停止境界を模した残留記録で起動を止め、CLIと同じ手順で元へ戻す</summary>
    [Test]
    public async Task InterruptedRestoreBlocksStartupUntilRollback()
    {
        app.PreserveData = true; await app.DisposeAsync();
        var id = Guid.NewGuid(); var previous = Path.Combine(app.DataDirectory, ".rollback-" + id.ToString("N")); Directory.CreateDirectory(previous);
        File.Move(Path.Combine(app.DataDirectory, "goods-manager.db"), Path.Combine(previous, "goods-manager.db"));
        await File.WriteAllBytesAsync(Path.Combine(app.DataDirectory, "restore-state.json"), JsonSerializer.SerializeToUtf8Bytes(new RestoreJournal(id, "switching", ["goods-manager.db", "images", "keys"]), BackupArchive.Json));
        using var runtime = new DatabaseRuntime(app.DataDirectory); Assert.Throws<InvalidOperationException>(runtime.AcquireLease);
        runtime.AcquireLease(true); new BackupRestore(runtime, Options()).Rollback(); runtime.Dispose();
        Assert.That(File.Exists(Path.Combine(app.DataDirectory, "goods-manager.db")), Is.True); app = new TestApplication(app.DataDirectory);
    }

    /// <summary>削除確認なしでは拒否し、受付と同じキーの再送でも対象を増やさない</summary>
    [Test]
    public async Task DeleteRequiresConfirmationAndIsResumable()
    {
        var id = await Create(); await Run(id); var path = await PathFor(id); var key = Guid.NewGuid();
        Assert.That((await Write("backups/" + id, new { }, Guid.NewGuid(), HttpMethod.Delete)).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(File.Exists(path), Is.True);
        Assert.That((await Write("backups/" + id, new { }, key, HttpMethod.Delete, confirmation: id.ToString("D"))).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That((await Write("backups/" + id, new { }, key, HttpMethod.Delete, confirmation: id.ToString("D"))).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        await using var scope = app.Services.CreateAsyncScope(); await scope.ServiceProvider.GetRequiredService<BackupService>().ProcessDeletionsAsync(default);
        Assert.That(File.Exists(path), Is.False); Assert.That(await State(id), Is.EqualTo("deleted"));
        Assert.That((await owner.GetAsync($"/api/v1/backups/{id}/download")).StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    /// <summary>改変したZIPはダウンロードさせず、失敗履歴と警告へ反映</summary>
    [Test]
    public async Task DownloadRevalidatesTheArchiveAndPreservesOtherFiles()
    {
        var id = await Create(); await Run(id); var path = await PathFor(id);
        var replacement = path + ".changed"; Rewrite(path, replacement, manifest => manifest with { DataRevision = manifest.DataRevision + 1 }); File.Move(replacement, path, true);
        Assert.That((await owner.GetAsync($"/api/v1/backups/{id}/download")).StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(await State(id), Is.EqualTo("failed")); Assert.That(File.Exists(imagePath), Is.True);
        await using var scope = app.Services.CreateAsyncScope();
        Assert.That(await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<SystemAlert>().AnyAsync(x => x.Code == "BACKUP_ARCHIVE_UNAVAILABLE" && x.ResolvedAt == null), Is.True);
    }

    /// <summary>鍵変更だけでも変更番号へ反映し、同じ鍵の再保全では履歴ループを作らない</summary>
    [Test]
    public async Task KeyChangesAreTrackedSeparatelyFromBackupHistory()
    {
        var first = await Create(); await Run(first); var before = await Revision();
        await File.WriteAllTextAsync(Path.Combine(app.DataDirectory, "keys", "extra.xml"), "<revocation />");
        var second = await Create(); await Run(second); Assert.That(await Revision(), Is.EqualTo(before + 1));
        var third = await Create(); await Run(third); Assert.That(await Revision(), Is.EqualTo(before + 1));
        using var zip = ZipFile.OpenRead(await PathFor(second)); Assert.That(zip.GetEntry("keys/extra.xml"), Is.Not.Null);
    }

    /// <summary>現DBが壊れていても、WAL・SHMを含む旧状態を改変せず退避して復元できる</summary>
    [Test]
    public async Task CorruptCurrentDatabaseDoesNotPreventRestoringAValidBackup()
    {
        var id = await Create(); await Run(id); var path = await PathFor(id);
        app.PreserveData = true; await app.DisposeAsync();
        var database = Path.Combine(app.DataDirectory, "goods-manager.db");
        await File.WriteAllBytesAsync(database, [1, 2, 3]);
        await File.WriteAllBytesAsync(database + "-wal", [4, 5]); await File.WriteAllBytesAsync(database + "-shm", [6, 7]);
        using (var runtime = new DatabaseRuntime(app.DataDirectory))
        {
            runtime.AcquireLease(); var receipt = await new BackupRestore(runtime, Options()).RestoreAsync(path, default);
            var previous = Path.Combine(app.DataDirectory, ".rollback-" + receipt.ToString("N"));
            Assert.That(await File.ReadAllBytesAsync(Path.Combine(previous, "goods-manager.db")), Is.EqualTo(new byte[] { 1, 2, 3 }));
            Assert.That(await File.ReadAllBytesAsync(Path.Combine(previous, "goods-manager.db-wal")), Is.EqualTo(new byte[] { 4, 5 }));
        }
        app = new TestApplication(app.DataDirectory); owner.Dispose(); owner = Client(app);
        Assert.That((await owner.GetAsync("/health/ready")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    /// <summary>本人の履歴とZIPは匿名取得不可。認証済みでもCSRFなしでは受付しない</summary>
    [Test]
    public async Task BackupEndpointsRequireAuthenticationAndCsrf()
    {
        using var anonymous = Client(app);
        foreach (var path in new[] { "/api/v1/settings/backups", "/api/v1/backups", "/api/v1/backups/" + Guid.NewGuid() + "/download" })
            Assert.That((await anonymous.GetAsync(path)).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That((await owner.PostAsJsonAsync("/api/v1/backups", new { kind = "manual" })).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }


    /// <summary>ZIP64を許容し、宣言件数を小さく偽装した中央ディレクトリは拒否</summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task ZipDirectoryChecksRealEntryCountAndZip64(bool forgedCount)
    {
        var id = await Create(); await Run(id); var bytes = await File.ReadAllBytesAsync(await PathFor(id));
        var end = bytes.Length - 22; var record = bytes.AsSpan(end).ToArray(); using var output = new MemoryStream();
        output.Write(bytes.AsSpan(0, end));
        if (forgedCount)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(8), 2);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(10), 2);
        }
        else
        {
            var zip64 = new byte[56]; var locator = new byte[20];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(zip64, 0x06064b50);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(zip64.AsSpan(4), 44);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(zip64.AsSpan(24), System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(10)));
            System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(zip64.AsSpan(32), System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(10)));
            System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(zip64.AsSpan(40), System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(12)));
            System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(zip64.AsSpan(48), System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(16)));
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(locator, 0x07064b50);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(locator.AsSpan(8), (ulong)end);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(locator.AsSpan(16), 1);
            output.Write(zip64); output.Write(locator);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(8), ushort.MaxValue);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(10), ushort.MaxValue);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(12), uint.MaxValue);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(16), uint.MaxValue);
        }
        output.Write(record); output.Position = 0;
        if (forgedCount) Assert.ThrowsAsync<InvalidDataException>(async () => await BackupArchive.VerifyAsync(output, Options(), null, default));
        else Assert.That((await BackupArchive.VerifyAsync(output, Options(), null, default)).RunId, Is.EqualTo(id));
    }


    /// <summary>P10の正規スキーマを含むZIPは、準備領域だけを現行版へ移行して復元</summary>
    [Test]
    public async Task RestoreMigratesAnOlderSupportedSchemaBeforeSwitching()
    {
        app.PreserveData = true; await app.DisposeAsync();
        using (var runtime = new DatabaseRuntime(app.DataDirectory))
        {
            runtime.AcquireLease();
            await using (var old = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(runtime.ConnectionString).Options))
            {
                await old.Database.OpenConnectionAsync();
                await BackupSnapshot.SanitizeAsync((SqliteConnection)old.Database.GetDbConnection(), default);
                await old.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>().MigrateAsync("20261018122826_TrashAndDeletionResponses");
                DatabaseMaintenance.CreateFullSnapshot((SqliteConnection)old.Database.GetDbConnection(), app.DataDirectory, Options(), new object());
            }
            SqliteConnection.ClearAllPools();
            var archive = Directory.GetFiles(Path.Combine(app.DataDirectory, "pre-migration"), "*.zip").Single();
            await new BackupRestore(runtime, Options()).RestoreAsync(archive, default);
        }
        app = new TestApplication(app.DataDirectory); owner.Dispose(); owner = Client(app);
        Assert.That((await owner.GetAsync("/health/ready")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        await using var scope = app.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.That(await db.Set<BackupSettings>().CountAsync(), Is.EqualTo(1)); Assert.That(await db.Set<ProductImage>().CountAsync(), Is.EqualTo(1)); Assert.That(db.Database.HasPendingModelChanges(), Is.False);
    }

    /// <summary>移行履歴が正しくても実テーブルを欠くZIPは、現DBを置き換える前に拒否</summary>
    [Test]
    public async Task MissingActualSchemaIsRejectedWithoutReplacingCurrentData()
    {
        var id = await Create(); await Run(id); var path = await PathFor(id);
        var stage = Path.Combine(app.DataDirectory, ".test-schema"); Directory.CreateDirectory(stage);
        try
        {
            BackupManifest manifest;
            await using (var input = File.OpenRead(path)) manifest = await BackupArchive.VerifyAsync(input, Options(), stage, default);
            await using (var copy = BackupArchive.OpenDatabase(stage)) { await copy.OpenAsync(); await BackupArchive.ScalarAsync(copy, "DROP TABLE ProductLinks", default); }
            var bytes = await File.ReadAllBytesAsync(Path.Combine(stage, "goods-manager.db"));
            manifest = manifest with { Files = manifest.Files.Select(x => x.Path == "goods-manager.db" ? x with { Size = bytes.Length, Sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)) } : x).ToArray() };
            var invalid = Path.Combine(app.DataDirectory, "invalid-schema.zip"); await BackupArchive.CreateAsync(stage, invalid, manifest, Options(), default);
            await Change(); app.PreserveData = true; await app.DisposeAsync();
            using (var runtime = new DatabaseRuntime(app.DataDirectory))
            {
                runtime.AcquireLease();
                Assert.ThrowsAsync<InvalidDataException>(async () => await new BackupRestore(runtime, Options()).RestoreAsync(invalid, default));
                await using var current = BackupArchive.OpenDatabase(app.DataDirectory, true); await current.OpenAsync();
                Assert.That(await BackupArchive.ScalarAsync(current, "SELECT Name FROM Products", default), Is.EqualTo("変更後"));
            }
            app = new TestApplication(app.DataDirectory);
        }
        finally { BackupPaths.DeleteTree(app.DataDirectory, stage); }
    }


    private BackupOptions Options() => new(new ConfigurationBuilder().Build(), new DatabaseRuntime(app.DataDirectory));
    private async Task<HttpResponseMessage> Write(string path, object body, Guid key, HttpMethod? method = null, string? etag = null, string? confirmation = null)
    {
        var csrf = await owner.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf"); using var request = new HttpRequestMessage(method ?? HttpMethod.Post, "/api/v1/" + path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-Token", csrf.GetProperty("token").GetString()); request.Headers.Add("Idempotency-Key", key.ToString("D")); if (etag is not null) request.Headers.TryAddWithoutValidation("If-Match", etag); if (confirmation is not null) request.Headers.Add("X-Confirm-Delete", confirmation); return await owner.SendAsync(request);
    }
    private async Task<Guid> Create() { var result = await Write("backups", new { kind = "manual" }, Guid.NewGuid()); Assert.That(result.StatusCode, Is.EqualTo(HttpStatusCode.Accepted), await result.Content.ReadAsStringAsync()); return (await result.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(); }
    private async Task Run(Guid id) { await using var scope = app.Services.CreateAsyncScope(); await scope.ServiceProvider.GetRequiredService<BackupService>().RunAsync(id, default); }
    private async Task Tick() { await using var scope = app.Services.CreateAsyncScope(); await scope.ServiceProvider.GetRequiredService<BackupService>().TickAsync(default); }
    private async Task<long> Revision() { await using var scope = app.Services.CreateAsyncScope(); return (await scope.ServiceProvider.GetRequiredService<AppDbContext>().SystemStates.SingleAsync()).DataRevision; }
    private async Task<int> Count(string kind, string state) { await using var scope = app.Services.CreateAsyncScope(); return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<BackupRun>().CountAsync(x => x.Kind == kind && x.State == state); }
    private async Task<string> State(Guid id) { await using var scope = app.Services.CreateAsyncScope(); return (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<BackupRun>().SingleAsync(x => x.Id == id)).State; }
    private async Task<string> PathFor(Guid id) { await using var scope = app.Services.CreateAsyncScope(); var run = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<BackupRun>().SingleAsync(x => x.Id == id); Assert.That(run.State, Is.EqualTo("succeeded"), run.ErrorCode); return scope.ServiceProvider.GetRequiredService<BackupService>().Resolve(run); }
    private async Task Change() { await using var scope = app.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var product = await db.Set<Product>().SingleAsync(x => x.Id == productId); product.Name = "変更後"; var state = await db.SystemStates.SingleAsync(); state.DataRevision++; await db.SaveChangesAsync(); }
    private static void Rewrite(string source, string target, Func<BackupManifest, BackupManifest> change, string? defect = null)
    {
        using var input = ZipFile.OpenRead(source); using var output = ZipFile.Open(target, ZipArchiveMode.Create);
        foreach (var entry in input.Entries)
        {
            using var original = entry.Open(); using var copy = output.CreateEntry(entry.FullName).Open();
            if (entry.FullName == "manifest.json") JsonSerializer.Serialize(copy, change(JsonSerializer.Deserialize<BackupManifest>(original, BackupArchive.Json)!), BackupArchive.Json);
            else original.CopyTo(copy);
        }
        if (defect is "traversal" or "duplicate" or "symlink")
        {
            var entry = output.CreateEntry(defect == "traversal" ? "../escaped" : defect == "duplicate" ? "goods-manager.db" : "keys/link.xml");
            if (defect == "symlink") entry.ExternalAttributes = unchecked((int)0xA1FF0000);
            using var stream = entry.Open(); stream.WriteByte(1);
        }
    }
}
