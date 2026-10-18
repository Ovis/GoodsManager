using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using GoodsManager.Web.Images;
using GoodsManager.Web.Masters;
using GoodsManager.Web.Products;
using GoodsManager.Web.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using static GoodsManager.Tests.AuthenticationTests;

namespace GoodsManager.Tests;

public sealed class ImageRecoveryTests
{
    private sealed class PausedConverter : IImageConverter
    {
        public int Calls;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<ConvertedImage> ConvertAsync(ImageJob job, CancellationToken token) { Interlocked.Increment(ref Calls); Started.TrySetResult(); await Task.Delay(Timeout.Infinite, token); throw new InvalidOperationException(); }
    }
    [Test]
    public async Task InterruptedWorkerResumesWithIntakePolicyAfterRestart()
    {
        var pause = new PausedConverter(); string directory, cookie; Guid uploadId; long revision;
        await using (var first = new TestApplication { PreserveData = true, ConfigureTestServices = services => { services.RemoveAll<IImageConverter>(); services.AddSingleton<IImageConverter>(pause); } })
        {
            directory = first.DataDirectory; using var client = Client(first); var setup = await SetupAsync(client); cookie = setup.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("__Host-GoodsManager=")).Split(';')[0];
            var csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf"); using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/temporary-uploads"); request.Headers.Add("X-CSRF-Token", csrf.GetProperty("token").GetString()); request.Headers.Add("Client-Upload-Key", Guid.NewGuid().ToString()); var form = new MultipartFormDataContent(); form.Add(new ByteArrayContent(ImageConversionTests.Png()), "file", "image.png"); request.Content = form;
            var response = await client.SendAsync(request); Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted)); uploadId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            await pause.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await using var scope = first.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var settings = await db.Set<ImageSettings>().SingleAsync(); settings.KeepOriginal = true; settings.DisplayMaxEdge = 100; settings.ThumbMaxEdge = 50; await db.SaveChangesAsync(); revision = (await db.SystemStates.SingleAsync()).DataRevision;
        }
        await using var restarted = new TestApplication(directory); using var restored = Client(restarted); restored.DefaultRequestHeaders.Add("Cookie", cookie);
        JsonElement status = default; for (var i = 0; i < 200; i++) { status = await restored.GetFromJsonAsync<JsonElement>($"/api/v1/temporary-uploads/{uploadId}"); if (status.GetProperty("state").GetString() is not ("pending" or "processing")) break; await Task.Delay(25); }
        Assert.That(status.GetProperty("state").GetString(), Is.EqualTo("ready"), status.ToString());
        await using var afterScope = restarted.Services.CreateAsyncScope(); var after = afterScope.ServiceProvider.GetRequiredService<AppDbContext>(); var asset = await after.Set<ImageAsset>().SingleAsync(); Assert.That(asset.KeepOriginal, Is.False); Assert.That(asset.OriginalFileId, Is.Null); Assert.That((await after.Set<ImageFile>().SingleAsync(x => x.Id == asset.DisplayFileId)).Width, Is.EqualTo(640)); Assert.That((await after.SystemStates.SingleAsync()).DataRevision, Is.EqualTo(revision));
    }
    [Test]
    public async Task QuotaAllowsReplayAtLimitAndCancellationReleasesTheSlot()
    {
        var paused = new PausedConverter(); await using var app = new TestApplication { ConfigureTestServices = services => { services.RemoveAll<IImageConverter>(); services.AddSingleton<IImageConverter>(paused); services.RemoveAll<ImageLimits>(); services.AddSingleton(new ImageLimits { TemporaryMaxCount = 1 }); } }; using var client = Client(app); await SetupAsync(client); var key = Guid.NewGuid();
        async Task<HttpResponseMessage> Upload(Guid uploadKey)
        {
            var csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf"); using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/temporary-uploads"); request.Headers.Add("X-CSRF-Token", csrf.GetProperty("token").GetString()); request.Headers.Add("Client-Upload-Key", uploadKey.ToString()); var form = new MultipartFormDataContent(); form.Add(new ByteArrayContent(ImageConversionTests.Png()), "file", "image.png"); request.Content = form; return await client.SendAsync(request);
        }
        var first = await Upload(key); Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.Accepted)); var id = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(); await paused.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That((await Upload(Guid.NewGuid())).StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests)); Assert.That((await Upload(key)).StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
        Assert.That((await SendAsync(client, $"temporary-uploads/{id}", method: HttpMethod.Delete)).StatusCode, Is.EqualTo(HttpStatusCode.NoContent)); Assert.That((await SendAsync(client, $"temporary-uploads/{id}", method: HttpMethod.Delete)).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That((await Upload(Guid.NewGuid())).StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
        for (var i = 0; i < 200 && Volatile.Read(ref paused.Calls) < 2; i++) await Task.Delay(25);
        Assert.That(Volatile.Read(ref paused.Calls), Is.EqualTo(2), "取消で変換が停止し、次の待機画像へ進む");
        Assert.That((await client.GetFromJsonAsync<JsonElement>($"/api/v1/temporary-uploads/{id}")).GetProperty("state").GetString(), Is.EqualTo("cancelled"));
    }
    [Test]
    public async Task TemporaryByteBudgetRejectsBeforeReceivingAndMemoryMonitorKillsChild()
    {
        await using var app = new TestApplication { ConfigureTestServices = services => { services.RemoveAll<ImageLimits>(); services.AddSingleton(new ImageLimits { TemporaryMaxBytes = 1 }); } }; using var client = Client(app); await SetupAsync(client); var csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf"); using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/temporary-uploads"); request.Headers.Add("X-CSRF-Token", csrf.GetProperty("token").GetString()); request.Headers.Add("Client-Upload-Key", Guid.NewGuid().ToString()); var form = new MultipartFormDataContent(); form.Add(new ByteArrayContent(ImageConversionTests.Png()), "file", "image.png"); request.Content = form; Assert.That((await client.SendAsync(request)).StatusCode, Is.EqualTo((HttpStatusCode)507));
        var files = app.Services.GetRequiredService<IImageFiles>(); var stage = files.NewStagingPath(); await File.WriteAllBytesAsync(files.Resolve(stage), ImageConversionTests.Png()); var job = new ImageJob(files.Resolve(stage), files.NewJobDirectory(), ImagePolicy.From(new ImageSettings()), 31457280, 40000000, 16384);
        var error = Assert.ThrowsAsync<ImageProblem>(async () => await new IsolatedImageConverter(new ImageLimits { WorkerMemoryBytes = 1 }).ConvertAsync(job, default)); Assert.That(error!.Message, Is.EqualTo("IMAGE_MEMORY_LIMIT"));
    }
    [Test]
    public async Task P06UpgradePreservesBusinessRevisionKeysAndTicket()
    {
        string directory, cookie; long revision;
        await using (var first = new TestApplication { PreserveData = true })
        {
            directory = first.DataDirectory; using var client = Client(first); var setup = await SetupAsync(client); cookie = setup.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("__Host-GoodsManager=")).Split(';')[0];
            await using var scope = first.Services.CreateAsyncScope(); revision = (await scope.ServiceProvider.GetRequiredService<AppDbContext>().SystemStates.SingleAsync()).DataRevision;
        }
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(new DatabaseRuntime(directory).ConnectionString).Options;
        await using (var previous = new AppDbContext(options)) await previous.GetService<IMigrator>().MigrateAsync("20261018072500_SearchFoundation");
        var keys = Directory.GetFiles(Path.Combine(directory, "keys")).ToDictionary(x => Path.GetFileName(x)!, File.ReadAllText);
        await using var upgraded = new TestApplication(directory); using var restored = Client(upgraded); restored.DefaultRequestHeaders.Add("Cookie", cookie); Assert.That((await restored.GetAsync("/api/v1/auth/session")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        await using var afterScope = upgraded.Services.CreateAsyncScope(); var db = afterScope.ServiceProvider.GetRequiredService<AppDbContext>(); Assert.That((await db.SystemStates.SingleAsync()).DataRevision, Is.EqualTo(revision)); Assert.That((await db.Set<ImageSettings>().SingleAsync()).KeepOriginal, Is.False); Assert.That(db.Database.HasPendingModelChanges(), Is.False);
        foreach (var key in keys) Assert.That(File.ReadAllText(Path.Combine(directory, "keys", key.Key!)), Is.EqualTo(key.Value));
        using var zip = ZipFile.OpenRead(Directory.GetFiles(Path.Combine(directory, "pre-migration"), "*.zip").Single()); Assert.That(zip.GetEntry("goods-manager.db"), Is.Not.Null);
    }
    [Test]
    public async Task OrphanGcReclaimsOnlyManagedExpiredPathsAndLeavesActiveJobs()
    {
        await using var app = new TestApplication(); using var client = Client(app); await SetupAsync(client); var files = app.Services.GetRequiredService<IImageFiles>(); var activity = app.Services.GetRequiredService<ImageActivity>();
        var staging = files.NewStagingPath(); await File.WriteAllBytesAsync(files.Resolve(staging), [1]); File.SetLastWriteTimeUtc(files.Resolve(staging), app.Clock.GetUtcNow().AddDays(-2).UtcDateTime);
        var hash = new string('a', 64); var path = files.Resolve(ImageFiles.ObjectPath(hash)); Directory.CreateDirectory(Path.GetDirectoryName(path)!); await File.WriteAllBytesAsync(path, [2]); File.SetLastWriteTimeUtc(path, app.Clock.GetUtcNow().AddDays(-2).UtcDateTime);
        var partial = path + "." + Guid.NewGuid().ToString("N") + ".partial"; await File.WriteAllBytesAsync(partial, [3]); File.SetLastWriteTimeUtc(partial, app.Clock.GetUtcNow().AddDays(-2).UtcDateTime);
        var stale = files.NewJobDirectory(); await File.WriteAllTextAsync(Path.Combine(stale, "job.json"), "{}"); Directory.SetLastWriteTimeUtc(stale, app.Clock.GetUtcNow().AddDays(-2).UtcDateTime);
        var active = files.NewJobDirectory(); await File.WriteAllTextAsync(Path.Combine(active, "job.json"), "{}"); Directory.SetLastWriteTimeUtc(active, app.Clock.GetUtcNow().AddDays(-2).UtcDateTime); activity.Jobs.Add(active);
        await using var scope = app.Services.CreateAsyncScope(); await scope.ServiceProvider.GetRequiredService<ImageGc>().RunAsync(default);
        Assert.That(File.Exists(path), Is.False); Assert.That(File.Exists(partial), Is.False); Assert.That(File.Exists(files.Resolve(staging)), Is.False); Assert.That(Directory.Exists(stale), Is.False); Assert.That(Directory.Exists(active), Is.True);
        Assert.Throws<InvalidDataException>(() => files.Resolve("../../outside")); Assert.Throws<InvalidDataException>(() => files.Resolve("objects/aa/" + hash + "/../outside"));
    }
    [Test]
    public async Task PreMigrationSnapshotContainsFormalImagesIncludingTrashAndRejectsMissingBytes()
    {
        await using var app = new TestApplication(); using var client = Client(app); await SetupAsync(client); await using var scope = app.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var files = scope.ServiceProvider.GetRequiredService<IImageFiles>(); var now = app.Clock.GetUtcNow(); var bytes = ImageConversionTests.Png(); var stage = files.NewStagingPath(); await File.WriteAllBytesAsync(files.Resolve(stage), bytes); var sha = await files.HashAsync(files.Resolve(stage), default); await files.PublishAsync(files.Resolve(stage), sha, default);
        var file = new ImageFile { Id = Guid.NewGuid(), Sha256 = sha, RelativePath = ImageFiles.ObjectPath(sha), MediaType = "image/png", ByteLength = bytes.Length, Width = 640, Height = 320, CreatedAt = now, UpdatedAt = now }; db.Add(file); var policy = ImagePolicy.From(new ImageSettings()); var asset = new ImageAsset { Id = Guid.NewGuid(), SourceSha256 = sha, PolicyHash = policy.Hash, PolicyJson = policy.Json, DisplayFileId = file.Id, ThumbnailFileId = file.Id, CreatedAt = now }; db.Add(asset);
        var product = new Product { Id = Guid.NewGuid(), Name = "ゴミ箱の商品", NormalizedName = "ゴミ箱の商品", CategoryId = await db.Set<Category>().Select(x => x.Id).FirstAsync(), CreatedAt = now, UpdatedAt = now, DeletedAt = now }; db.Add(product); db.Add(new ProductImage { Id = Guid.NewGuid(), ProductId = product.Id, ImageAssetId = asset.Id, IsPrimary = true, CreatedAt = now, UpdatedAt = now }); await db.SaveChangesAsync();
        var gate = scope.ServiceProvider.GetRequiredService<GoodsManager.Web.Auth.AuthWriteGate>(); await gate.Semaphore.WaitAsync();
        try
        {
            await db.Database.OpenConnectionAsync(); var method = typeof(DatabaseMaintenance).GetMethod("CreateSnapshot", BindingFlags.NonPublic | BindingFlags.Static)!; method.Invoke(null, [db.Database.GetDbConnection(), app.DataDirectory]);
            using var zip = ZipFile.OpenRead(Directory.GetFiles(Path.Combine(app.DataDirectory, "pre-migration"), "*.zip").Single()); Assert.That(zip.Entries.Count(x => x.FullName.StartsWith("images/")), Is.EqualTo(1)); using var content = zip.GetEntry("images/" + file.RelativePath)!.Open(); using var copy = new MemoryStream(); await content.CopyToAsync(copy); Assert.That(copy.ToArray(), Is.EqualTo(bytes));
            files.Delete(file.RelativePath); Assert.Throws<TargetInvocationException>(() => method.Invoke(null, [db.Database.GetDbConnection(), app.DataDirectory])); Assert.That(Directory.GetFiles(Path.Combine(app.DataDirectory, "pre-migration"), "*.zip").Length, Is.EqualTo(1));
        }
        finally { gate.Semaphore.Release(); }
    }
}
