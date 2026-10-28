using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoodsManager.Web.Images;
using GoodsManager.Web.Masters;
using GoodsManager.Web.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static GoodsManager.Tests.AuthenticationTests;

namespace GoodsManager.Tests;

public sealed class ImageTests
{
    private TestApplication app = null!;
    private HttpClient client = null!;
    [SetUp] public async Task Start() { app = new(); client = Client(app); await SetupAsync(client); }
    [TearDown] public async Task Stop() { client.Dispose(); await app.DisposeAsync(); }
    private async Task<T> Db<T>(Func<AppDbContext, Task<T>> action) { await using var scope = app.Services.CreateAsyncScope(); return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>()); }
    private async Task<long> Revision() => await Db(async db => (await db.SystemStates.AsNoTracking().SingleAsync()).DataRevision);
    private async Task<HttpResponseMessage> Write(string path, object body, string? etag = null, Guid? key = null, HttpMethod? method = null)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
        using var request = new HttpRequestMessage(method ?? HttpMethod.Put, "/api/v1/" + path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-Token", csrf.GetProperty("token").GetString()); request.Headers.Add("Idempotency-Key", (key ?? Guid.NewGuid()).ToString());
        if (etag is not null) request.Headers.TryAddWithoutValidation("If-Match", etag);
        return await client.SendAsync(request);
    }
    private static async Task<JsonElement> Ok(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        Assert.That(response.StatusCode, Is.EqualTo(expected), await response.Content.ReadAsStringAsync()); return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    private static Guid Id(JsonElement x) => x.GetProperty("id").GetGuid();
    private static string Etag(JsonElement x) => x.GetProperty("etag").GetString()!;
    private async Task<JsonElement> Product()
    {
        var category = await Db(async db => { db.CollectionId = Guid.Parse(client.DefaultRequestHeaders.GetValues("X-Collection-Id").Single()); return await db.Set<Category>().Select(x => x.Id).FirstAsync(); });
        return await Ok(await Write("dev/products", new { product = new { name = "写真試験", categoryId = category, workIds = Array.Empty<Guid>(), characterIds = Array.Empty<Guid>(), tagIds = Array.Empty<Guid>(), identifiers = Array.Empty<object>(), links = Array.Empty<object>() }, initialLot = new { quantity = 2, fields = new { condition = "unopened", storageLocation = (string?)null, purchasePlace = (string?)null, memo = (string?)null } } }, method: HttpMethod.Post), HttpStatusCode.Created);
    }
    private async Task<HttpResponseMessage> UploadResponse(byte[] bytes, Guid? key = null, bool csrf = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/temporary-uploads");
        if (csrf) request.Headers.Add("X-CSRF-Token", (await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("token").GetString());
        request.Headers.Add("Client-Upload-Key", (key ?? Guid.NewGuid()).ToString());
        var multipart = new MultipartFormDataContent(); var content = new ByteArrayContent(bytes); content.Headers.ContentType = new("text/plain"); multipart.Add(content, "file", "../../untrusted.jpg"); request.Content = multipart;
        return await client.SendAsync(request);
    }
    private async Task<JsonElement> Upload(byte[]? bytes = null, Guid? key = null)
    {
        var result = await Ok(await UploadResponse(bytes ?? ImageConversionTests.Png(), key), HttpStatusCode.Accepted);
        var timer = Stopwatch.StartNew();
        while (result.GetProperty("state").GetString() is "pending" or "processing")
        {
            Assert.That(timer.Elapsed, Is.LessThan(TimeSpan.FromSeconds(10)), result.ToString()); await Task.Delay(25);
            result = await client.GetFromJsonAsync<JsonElement>($"/api/v1/temporary-uploads/{Id(result)}");
        }
        return result;
    }
    private Task<JsonElement> Images(Guid productId) => client.GetFromJsonAsync<JsonElement>($"/api/v1/products/{productId}/images");
    private Task<HttpResponseMessage> Replace(Guid id, object[] entries, string etag, string? primary = null, Guid? key = null) => Write($"products/{id}/images", new { entries, primaryRef = primary }, etag, key);
    private static object New(JsonElement upload, string reference = "new", int sort = 0) => new { clientRef = reference, temporaryUploadId = Id(upload), sortOrder = sort };
    private static object Existing(JsonElement entry, int? sort = null, Guid? replacement = null) => new { linkId = entry.GetProperty("linkId").GetGuid(), sortOrder = sort ?? entry.GetProperty("sortOrder").GetInt32(), replacementUploadId = replacement };
    private async Task Gc() { await using var scope = app.Services.CreateAsyncScope(); await scope.ServiceProvider.GetRequiredService<ImageGc>().RunAsync(default); }

    [Test]
    public async Task UploadSniffsActualBytesReplaysAndRequiresAuthenticationAndCsrf()
    {
        var revision = await Revision(); var key = Guid.NewGuid(); var bytes = ImageConversionTests.Png(); var upload = await Upload(bytes, key);
        Assert.That(upload.GetProperty("state").GetString(), Is.EqualTo("ready"));
        var replay = await Ok(await UploadResponse(bytes, key), HttpStatusCode.Accepted); Assert.That(Id(replay), Is.EqualTo(Id(upload)));
        Assert.That((await UploadResponse(ImageConversionTests.Png(321, 123), key)).StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That((await UploadResponse(bytes, csrf: false)).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        var preview = await client.GetAsync(upload.GetProperty("previewUrl").GetString()); Assert.That(preview.Content.Headers.ContentType!.MediaType, Is.EqualTo("image/webp")); Assert.That(preview.Headers.CacheControl!.NoStore, Is.True);
        var asset = await Db(async db => await db.Set<TemporaryUpload>().Where(x => x.Id == Id(upload)).Select(x => x.ImageAssetId).SingleAsync());
        Assert.That((await client.GetAsync($"/media/assets/{asset}/display")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        using var anonymous = Client(app); Assert.That((await anonymous.GetAsync(upload.GetProperty("previewUrl").GetString())).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(await Revision(), Is.EqualTo(revision));
    }
    [Test]
    public async Task InvalidImagesFailWithoutAssetsAndRequireANewKeyToRetry()
    {
        var key = Guid.NewGuid(); var bad = await Upload("<svg/>"u8.ToArray(), key); Assert.That(bad.GetProperty("state").GetString(), Is.EqualTo("failed"));
        var replay = await Ok(await UploadResponse("<svg/>"u8.ToArray(), key), HttpStatusCode.Accepted); Assert.That(replay.GetProperty("state").GetString(), Is.EqualTo("failed"));
        Assert.That((await UploadResponse([])).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That((await UploadResponse(new byte[30 * 1024 * 1024 + 1])).StatusCode, Is.EqualTo(HttpStatusCode.RequestEntityTooLarge));
        Assert.That(await Db(db => db.Set<ImageAsset>().CountAsync()), Is.Zero);
        Assert.That((await Upload()).GetProperty("state").GetString(), Is.EqualTo("ready"));
    }
    [Test]
    public async Task LinksSupportPrimaryOrderReplacementDeduplicationAndFrozenReplay()
    {
        var p = await Product(); var id = Id(p); var u1 = await Upload(); var u2 = await Upload(ImageConversionTests.Png(200, 500)); var revision = await Revision();
        var first = await Ok(await Replace(id, [New(u1, "a", 10), New(u2, "b", 20)], Etag(p), "b")); var entries = first.GetProperty("entries").EnumerateArray().ToArray();
        Assert.That(entries.Count(x => x.GetProperty("isPrimary").GetBoolean()), Is.EqualTo(1)); Assert.That(entries[1].GetProperty("isPrimary").GetBoolean(), Is.True); Assert.That(await Revision(), Is.EqualTo(revision + 1));
        var image = await client.GetAsync(entries[0].GetProperty("displayUrl").GetString()); Assert.That(image.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(entries[0].GetProperty("originalUrl").ValueKind, Is.EqualTo(JsonValueKind.Null)); Assert.That((await client.GetAsync($"/media/assets/{entries[0].GetProperty("assetId").GetGuid()}/original")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        var same = await Upload(); var noOp = await Ok(await Replace(id, [Existing(entries[0]), Existing(entries[1]), New(same, "same", 999)], Etag(first)));
        Assert.That(noOp.GetProperty("entries").GetArrayLength(), Is.EqualTo(2)); Assert.That(noOp.GetProperty("deduplicatedCount").GetInt32(), Is.EqualTo(1)); Assert.That(Etag(noOp), Is.EqualTo(Etag(first))); Assert.That(await Revision(), Is.EqualTo(revision + 1));
        var replacement = await Upload(ImageConversionTests.Png(300, 100)); var key = Guid.NewGuid(); object[] request = [Existing(entries[0], 5, Id(replacement)), Existing(entries[1], 1)];
        var changed = await Ok(await Replace(id, request, Etag(first), entries[0].GetProperty("linkId").GetString(), key));
        Assert.That(changed.GetProperty("entries")[1].GetProperty("linkId").GetGuid(), Is.EqualTo(entries[0].GetProperty("linkId").GetGuid()));
        await Ok(await Replace(id, [], Etag(changed))); Assert.That((await Images(id)).GetProperty("entries").GetArrayLength(), Is.Zero);
        var replay = await Ok(await Replace(id, request, Etag(first), entries[0].GetProperty("linkId").GetString(), key)); Assert.That(replay.ToString(), Is.EqualTo(changed.ToString()));
        Assert.That((await Replace(id, [], Etag(first))).StatusCode, Is.EqualTo(HttpStatusCode.PreconditionFailed));
        Assert.That((await client.GetAsync(entries[0].GetProperty("displayUrl").GetString())).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
    [Test]
    public async Task AssetSwapPreservesLinkIdsAndDoesNotHitUniqueConstraints()
    {
        var p = await Product(); var a = await Upload(); var b = await Upload(ImageConversionTests.Png(300, 500)); var first = await Ok(await Replace(Id(p), [New(a, "a"), New(b, "b", 1)], Etag(p)));
        var entries = first.GetProperty("entries").EnumerateArray().ToArray(); var aa = await Upload(); var bb = await Upload(ImageConversionTests.Png(300, 500));
        var swap = await Ok(await Replace(Id(p), [Existing(entries[0], 0, Id(bb)), Existing(entries[1], 1, Id(aa))], Etag(first)));
        Assert.That(swap.GetProperty("entries")[0].GetProperty("linkId").GetGuid(), Is.EqualTo(entries[0].GetProperty("linkId").GetGuid())); Assert.That(swap.GetProperty("entries")[0].GetProperty("assetId").GetGuid(), Is.EqualTo(entries[1].GetProperty("assetId").GetGuid()));
    }
    [Test]
    public async Task SettingsSnapshotSeparatesOriginalPolicyAndSharesDerivedFiles()
    {
        var off = await Upload(); var settings = await client.GetFromJsonAsync<JsonElement>("/api/v1/settings/images"); var rev = await Revision(); var key = Guid.NewGuid(); var body = new { keepOriginal = true, displayMaxEdge = 2048, displayQuality = 82, thumbMaxEdge = 320, thumbQuality = 75 };
        var updated = await Ok(await Write("settings/images", body, Etag(settings), key)); Assert.That(await Revision(), Is.EqualTo(rev + 1));
        Assert.That((await Write("settings/images", body, Etag(settings))).StatusCode, Is.EqualTo(HttpStatusCode.PreconditionFailed));
        Assert.That((await Ok(await Write("settings/images", body, Etag(settings), key))).ToString(), Is.EqualTo(updated.ToString()));
        var on = await Upload(); var p = await Product(); var result = await Ok(await Replace(Id(p), [New(off, "off"), New(on, "on", 1)], Etag(p))); var entries = result.GetProperty("entries").EnumerateArray().ToArray();
        Assert.That(entries[0].GetProperty("assetId").GetGuid(), Is.Not.EqualTo(entries[1].GetProperty("assetId").GetGuid()));
        Assert.That((await client.GetAsync(entries[1].GetProperty("originalUrl").GetString())).Content.Headers.ContentType!.MediaType, Is.EqualTo("image/png"));
        Assert.That((await client.GetAsync($"/media/assets/{entries[0].GetProperty("assetId").GetGuid()}/original")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(await Db(db => db.Set<ImageFile>().CountAsync()), Is.EqualTo(3));
    }
    [Test]
    public async Task ImageWriteRollsBackWhenFinalResponsePersistenceFails()
    {
        var p = await Product(); var upload = await Upload(); var rev = await Revision();
        await Db(async db => await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_image_response BEFORE INSERT ON IdempotencyRecords BEGIN SELECT RAISE(ABORT,'test'); END"));
        Assert.That((await Replace(Id(p), [New(upload)], Etag(p))).StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
        Assert.That((await Images(Id(p))).GetProperty("entries").GetArrayLength(), Is.Zero); Assert.That(await Revision(), Is.EqualTo(rev));
        Assert.That(await Db(async db => (await db.Set<TemporaryUpload>().SingleAsync(x => x.Id == Id(upload))).State), Is.EqualTo("ready"));
    }
    [Test]
    public async Task ItemOnlyImagesDoNotMatchProductPhotoFilterAndMoveAtomicallyOnReturn()
    {
        var p = await Product(); var upload = await Upload(); var id = Id(p); var lot = p.GetProperty("inventory").GetProperty("lots")[0].GetProperty("id").GetString();
        var result = await Ok(await Write($"products/{id}/inventory-commands", new { commands = new object[] { new { type = "extractItems", lotRef = lot, itemRefs = new[] { "one" } }, new { type = "replaceItemImages", itemRef = "one", entries = new[] { New(upload) } } } }, Etag(p), method: HttpMethod.Post));
        Assert.That(result.GetProperty("items")[0].GetProperty("images").GetArrayLength(), Is.EqualTo(1));
        var search = await client.GetFromJsonAsync<JsonElement>("/api/v1/products?hasImages=true"); Assert.That(search.GetProperty("totalCount").GetInt32(), Is.Zero);
        var item = result.GetProperty("createdRefs").GetProperty("one").GetString();
        var returned = await Ok(await Write($"products/{id}/inventory-commands", new { commands = new[] { new { type = "returnItem", itemRef = item, newLotRef = "lot", memoAction = "discardConfirmed", photoAction = "moveToProduct" } } }, Etag(result), method: HttpMethod.Post));
        Assert.That((await Images(id)).GetProperty("entries").GetArrayLength(), Is.EqualTo(1)); Assert.That(returned.GetProperty("totalQuantity").GetInt64(), Is.EqualTo(2));
        Assert.That((await Replace(id, [], Etag(result))).StatusCode, Is.EqualTo(HttpStatusCode.PreconditionFailed));
    }
    [Test]
    public async Task ExpiryAndGcRespectGraceAndReferencesAndCollectOrphans()
    {
        var p = await Product(); var upload = await Upload(); var linked = await Ok(await Replace(Id(p), [New(upload)], Etag(p))); var assetId = linked.GetProperty("entries")[0].GetProperty("assetId").GetGuid();
        app.Clock.Advance(TimeSpan.FromHours(25)); await Gc(); Assert.That(await Db(db => db.Set<ImageAsset>().AnyAsync(x => x.Id == assetId)), Is.True);
        await Ok(await Replace(Id(p), [], Etag(linked))); await Gc(); Assert.That(await Db(db => db.Set<ImageFile>().AllAsync(x => x.State == "deletePending")), Is.True);
        Assert.That(await Db(db => db.Set<ImageFile>().CountAsync()), Is.GreaterThan(0)); var stored = await Db(db => db.Set<ImageFile>().FirstAsync());
        var files = app.Services.GetRequiredService<IImageFiles>();
        await using (var locked = new FileStream(files.Resolve(stored.RelativePath), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            app.Clock.Advance(TimeSpan.FromHours(25)); await Gc();
            Assert.That(await Db(db => db.Set<ImageFile>().AnyAsync(x => x.Id == stored.Id && x.State == "deletePending")), Is.True);
            Assert.That(await Db(db => db.Set<SystemAlert>().AnyAsync(x => x.Code == "IMAGE_GC_FAILED" && x.ResolvedAt == null)), Is.True);
        }
        await Gc(); Assert.That(await Db(db => db.Set<ImageFile>().CountAsync()), Is.Zero);
        Assert.That(await Db(db => db.Set<SystemAlert>().AnyAsync(x => x.Code == "IMAGE_GC_FAILED" && x.ResolvedAt == null)), Is.False);
        var expired = await Upload(); app.Clock.Advance(TimeSpan.FromHours(25)); var status = await client.GetFromJsonAsync<JsonElement>($"/api/v1/temporary-uploads/{Id(expired)}"); Assert.That(status.GetProperty("state").GetString(), Is.EqualTo("cancelled")); Assert.That(status.GetProperty("errorCode").GetString(), Is.EqualTo("UPLOAD_EXPIRED"));
        Assert.That((await client.GetAsync($"/api/v1/temporary-uploads/{Id(expired)}/preview")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
    [Test]
    public async Task MissingFileCreatesSafePersistentAlertAndReturns503()
    {
        var p = await Product(); var upload = await Upload(); var linked = await Ok(await Replace(Id(p), [New(upload)], Etag(p))); var entry = linked.GetProperty("entries")[0];
        var stored = await Db(async db => await db.Set<ImageFile>().SingleAsync(x => db.Set<ImageAsset>().Any(a => a.Id == entry.GetProperty("assetId").GetGuid() && a.DisplayFileId == x.Id)));
        var files = app.Services.GetRequiredService<IImageFiles>(); var bytes = await File.ReadAllBytesAsync(files.Resolve(stored.RelativePath)); files.Delete(stored.RelativePath);
        Assert.That((await client.GetAsync(entry.GetProperty("displayUrl").GetString())).StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
        Assert.That((await client.GetAsync("/health/ready")).StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
        var alerts = await client.GetFromJsonAsync<JsonElement>("/api/v1/system/alerts"); Assert.That(alerts.GetProperty("entries").GetArrayLength(), Is.EqualTo(1)); Assert.That(alerts.ToString(), Does.Not.Contain(app.DataDirectory));
        await File.WriteAllBytesAsync(files.Resolve(stored.RelativePath), bytes); Assert.That((await client.GetAsync(entry.GetProperty("displayUrl").GetString())).StatusCode, Is.EqualTo(HttpStatusCode.OK)); Assert.That((await client.GetFromJsonAsync<JsonElement>("/api/v1/system/alerts")).GetProperty("entries").GetArrayLength(), Is.Zero);
    }
    [Test]
    public async Task SameImageUsesCollectionOwnedAssetsAndGcKeepsAnotherCollectionsFormalPhoto()
    {
        var uploadKey = Guid.NewGuid(); var originalUpload = await Upload(key: uploadKey); var originalId = Id(originalUpload);
        var originalSettings = await client.GetFromJsonAsync<JsonElement>("/api/v1/settings/images");
        var space = await Ok(await Write("collections", new { name = "画像を分離", importFrom = GoodsManager.Web.Collections.CollectionSchema.DefaultId }, method: HttpMethod.Post), HttpStatusCode.Created);
        var collectionId = Id(space); client.DefaultRequestHeaders.Remove("X-Collection-Id"); client.DefaultRequestHeaders.Add("X-Collection-Id", collectionId.ToString());
        var copiedSettings = await client.GetFromJsonAsync<JsonElement>("/api/v1/settings/images"); Assert.That(copiedSettings.GetProperty("displayQuality").GetInt32(), Is.EqualTo(originalSettings.GetProperty("displayQuality").GetInt32()));
        var upload = await Upload(key: uploadKey); Assert.That(Id(upload), Is.Not.EqualTo(originalId));
        Guid originalAsset, targetAsset;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var first = await db.Set<TemporaryUpload>().SingleAsync(x => x.Id == originalId); var second = await db.Set<TemporaryUpload>().SingleAsync(x => x.Id == Id(upload)); originalAsset = first.ImageAssetId!.Value; targetAsset = second.ImageAssetId!.Value;
            Assert.That(targetAsset, Is.Not.EqualTo(originalAsset));
            var a = await db.Set<ImageAsset>().SingleAsync(x => x.Id == originalAsset); var b = await db.Set<ImageAsset>().SingleAsync(x => x.Id == targetAsset); Assert.That(a.DisplayFileId, Is.EqualTo(b.DisplayFileId)); Assert.That((Guid)db.Entry(b).Property("CollectionId").CurrentValue!, Is.EqualTo(collectionId));
        }
        var p = await Product();
        Assert.That((await Replace(Id(p), [New(originalUpload)], Etag(p))).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        var linked = await Ok(await Replace(Id(p), [New(upload)], Etag(p))); var url = linked.GetProperty("entries")[0].GetProperty("displayUrl").GetString();
        Assert.That(url, Does.Contain("collectionId=" + collectionId));
        await Ok(await Write("settings/images", new { keepOriginal = true, displayMaxEdge = 2048, displayQuality = 81, thumbMaxEdge = 320, thumbQuality = 75 }, Etag(copiedSettings)));
        client.DefaultRequestHeaders.Remove("X-Collection-Id"); client.DefaultRequestHeaders.Add("X-Collection-Id", GoodsManager.Web.Collections.CollectionSchema.DefaultId.ToString());
        Assert.That((await client.GetFromJsonAsync<JsonElement>("/api/v1/settings/images")).GetProperty("keepOriginal").GetBoolean(), Is.False);
        Assert.That((await Write($"temporary-uploads/{originalId}", new { }, method: HttpMethod.Delete)).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        await Gc(); app.Clock.Advance(TimeSpan.FromHours(25)); await Gc();
        client.DefaultRequestHeaders.Remove("X-Collection-Id"); Assert.That((await client.GetAsync(url)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        await using var finalScope = app.Services.CreateAsyncScope(); var after = finalScope.ServiceProvider.GetRequiredService<AppDbContext>(); Assert.That(await after.Set<ImageAsset>().AnyAsync(x => x.Id == originalAsset), Is.False); Assert.That(await after.Set<ImageAsset>().AnyAsync(x => x.Id == targetAsset), Is.True);
    }

}
