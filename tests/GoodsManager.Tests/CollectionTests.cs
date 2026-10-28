using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoodsManager.Web.Auth;
using GoodsManager.Web.Collections;
using GoodsManager.Web.Images;
using GoodsManager.Web.Masters;
using GoodsManager.Web.Products;
using GoodsManager.Web.Storage;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static GoodsManager.Tests.AuthenticationTests;

namespace GoodsManager.Tests;

public sealed class CollectionTests
{
    private static void Select(HttpClient client, Guid id) { client.DefaultRequestHeaders.Remove("X-Collection-Id"); client.DefaultRequestHeaders.Add("X-Collection-Id", id.ToString("D")); }
    private static async Task<HttpResponseMessage> Write(HttpClient client, string path, object body, HttpMethod? method = null, string? etag = null, Guid? key = null)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
        using var request = new HttpRequestMessage(method ?? HttpMethod.Post, "/api/v1/" + path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-Token", csrf.GetProperty("token").GetString()); request.Headers.Add("Idempotency-Key", (key ?? Guid.NewGuid()).ToString());
        if (etag is not null) request.Headers.TryAddWithoutValidation("If-Match", etag);
        return await client.SendAsync(request);
    }
    private static async Task<JsonElement> Created(HttpClient client, string path, object body, Guid? key = null)
    {
        var response = await Write(client, path, body, key: key); Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created), await response.Content.ReadAsStringAsync()); return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    private static object ProductBody(Guid category) => new { product = new { name = "分離テスト", categoryId = category, workIds = Array.Empty<Guid>(), characterIds = Array.Empty<Guid>(), tagIds = Array.Empty<Guid>(), identifiers = Array.Empty<object>(), links = Array.Empty<object>() }, initialLot = new { quantity = 1, fields = new { condition = (string?)null, storageLocation = (string?)null, purchasePlace = (string?)null, memo = (string?)null } }, images = Array.Empty<object>() };

    [Test]
    public async Task CreatesIndependentCollectionsAndRejectsForeignIdsAndCrossCollectionReferences()
    {
        await using var app = new TestApplication(); using var client = Client(app); var setup = await SetupAsync(client); Assert.That(setup.StatusCode, Is.EqualTo(HttpStatusCode.Created), await setup.Content.ReadAsStringAsync());
        var spaces = await client.GetFromJsonAsync<JsonElement>("/api/v1/collections"); Assert.That(spaces.GetProperty("items").GetArrayLength(), Is.EqualTo(1));
        var catA = await Created(client, "masters/categories", new { name = "Aだけ", parentId = (Guid?)null, sortOrder = 0 });
        var productA = await Created(client, "products", ProductBody(catA.GetProperty("id").GetGuid())); var productId = productA.GetProperty("product").GetProperty("id").GetGuid();
        var other = await Created(client, "collections", new { name = "別コレクション" }); var second = other.GetProperty("id").GetGuid(); Select(client, second);
        Assert.That((await client.GetFromJsonAsync<JsonElement>("/api/v1/categories/tree")).GetProperty("nodes").GetArrayLength(), Is.Zero);
        Assert.That((await client.GetFromJsonAsync<JsonElement>("/api/v1/products?inventory=all")).GetProperty("totalCount").GetInt64(), Is.Zero);
        Assert.That((await client.GetAsync($"/api/v1/products/{productId}")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await client.GetAsync("/api/v1/masters/categories/" + catA.GetProperty("id").GetGuid())).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        var rejected = await Write(client, "products", ProductBody(catA.GetProperty("id").GetGuid())); Assert.That(rejected.IsSuccessStatusCode, Is.False);
        var catB = await Created(client, "masters/categories", new { name = "Bだけ", parentId = (Guid?)null, sortOrder = 0 });
        await Created(client, "products", ProductBody(catB.GetProperty("id").GetGuid()));
        Assert.That((await client.GetFromJsonAsync<JsonElement>("/api/v1/products?inventory=all")).GetProperty("totalCount").GetInt64(), Is.EqualTo(1));
        Select(client, CollectionSchema.DefaultId); Assert.That((await client.GetFromJsonAsync<JsonElement>("/api/v1/products?inventory=all")).GetProperty("totalCount").GetInt64(), Is.EqualTo(1));
        Select(client, Guid.NewGuid()); Assert.That((await client.GetAsync("/api/v1/products?inventory=all")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        client.DefaultRequestHeaders.Remove("X-Collection-Id"); Assert.That((await client.GetAsync("/api/v1/products")).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task SettingsImportRekeysHierarchyAndRelationsAndKeepsCopiesIndependent()
    {
        await using var app = new TestApplication(); using var client = Client(app); await SetupAsync(client);
        var root = await Created(client, "masters/categories", new { name = "親A", parentId = (Guid?)null, sortOrder = 3 });
        await Created(client, "masters/categories", new { name = "子A", parentId = root.GetProperty("id").GetGuid(), sortOrder = 8 });
        var work = await Created(client, "masters/works", new { name = "作品A" }); var series = await Created(client, "masters/series", new { name = "シリーズA" }); var character = await Created(client, "masters/characters", new { name = "キャラA" });
        var relation = await Write(client, $"masters/works/{work.GetProperty("id").GetGuid()}/relations", new { addIds = new[] { series.GetProperty("id").GetGuid() }, removeIds = Array.Empty<Guid>() }, etag: work.GetProperty("etag").GetString()); Assert.That(relation.IsSuccessStatusCode, Is.True);
        relation = await Write(client, $"masters/characters/{character.GetProperty("id").GetGuid()}/relations", new { addIds = new[] { work.GetProperty("id").GetGuid() }, removeIds = Array.Empty<Guid>() }, etag: character.GetProperty("etag").GetString()); Assert.That(relation.IsSuccessStatusCode, Is.True);
        await Created(client, "masters/tags", new { name = "タグA" });
        await Created(client, "masters/organizations", new { name = "組織A", websiteUrl = "https://example.com/", memo = "組織のメモ" });
        var policy = await client.GetFromJsonAsync<JsonElement>("/api/v1/settings/images");
        Assert.That((await Write(client, "settings/images", new { keepOriginal = true, displayMaxEdge = 1600, displayQuality = 80, thumbMaxEdge = 240, thumbQuality = 70 }, HttpMethod.Put, policy.GetProperty("etag").GetString())).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var copy = await Created(client, "collections", new { name = "コピー先", importFrom = CollectionSchema.DefaultId }); Select(client, copy.GetProperty("id").GetGuid());
        var categories = (await client.GetFromJsonAsync<JsonElement>("/api/v1/categories/tree")).GetProperty("nodes").EnumerateArray().ToArray(); var copiedRoot = categories.Single(x => x.GetProperty("name").GetString() == "親A"); var copiedChild = categories.Single(x => x.GetProperty("name").GetString() == "子A");
        Assert.That(copiedRoot.GetProperty("id").GetGuid(), Is.Not.EqualTo(root.GetProperty("id").GetGuid())); Assert.That(copiedChild.GetProperty("parentId").GetGuid(), Is.EqualTo(copiedRoot.GetProperty("id").GetGuid())); Assert.That(copiedChild.GetProperty("sortOrder").GetInt32(), Is.EqualTo(8));
        var copiedWork = (await client.GetFromJsonAsync<JsonElement>("/api/v1/masters/works")).GetProperty("items")[0];
        var copiedRelations = await client.GetFromJsonAsync<JsonElement>($"/api/v1/masters/works/{copiedWork.GetProperty("id").GetGuid()}/relations"); Assert.That(copiedRelations.GetProperty("items")[0].GetProperty("id").GetGuid(), Is.Not.EqualTo(series.GetProperty("id").GetGuid()));
        Assert.That((await client.GetFromJsonAsync<JsonElement>("/api/v1/products?inventory=all")).GetProperty("totalCount").GetInt64(), Is.Zero);
        var copiedCharacter = (await client.GetFromJsonAsync<JsonElement>("/api/v1/masters/characters")).GetProperty("items")[0];
        Assert.That((await client.GetFromJsonAsync<JsonElement>($"/api/v1/masters/characters/{copiedCharacter.GetProperty("id").GetGuid()}/relations")).GetProperty("items")[0].GetProperty("id").GetGuid(), Is.EqualTo(copiedWork.GetProperty("id").GetGuid()));
        Assert.That((await client.GetFromJsonAsync<JsonElement>("/api/v1/masters/tags")).GetProperty("items")[0].GetProperty("name").GetString(), Is.EqualTo("タグA"));
        var copiedOrg = (await client.GetFromJsonAsync<JsonElement>("/api/v1/masters/organizations")).GetProperty("items")[0];
        Assert.That(copiedOrg.GetProperty("websiteUrl").GetString(), Is.EqualTo("https://example.com/")); Assert.That(copiedOrg.GetProperty("memo").GetString(), Is.EqualTo("組織のメモ"));
        var copiedPolicy = await client.GetFromJsonAsync<JsonElement>("/api/v1/settings/images"); Assert.That(copiedPolicy.GetProperty("keepOriginal").GetBoolean(), Is.True); Assert.That(copiedPolicy.GetProperty("displayMaxEdge").GetInt32(), Is.EqualTo(1600));
        Select(client, CollectionSchema.DefaultId); await Write(client, $"masters/categories/{root.GetProperty("id").GetGuid()}", new { name = "変更後", parentId = (Guid?)null, sortOrder = 3 }, HttpMethod.Put, root.GetProperty("etag").GetString());
        Select(client, copy.GetProperty("id").GetGuid()); Assert.That((await client.GetFromJsonAsync<JsonElement>($"/api/v1/masters/categories/{copiedRoot.GetProperty("id").GetGuid()}")).GetProperty("name").GetString(), Is.EqualTo("親A"));
    }

    [Test]
    public async Task DatabaseForeignKeysRejectCrossCollectionLinksAndReplayKeysAreScoped()
    {
        await using var app = new TestApplication(); using var client = Client(app); await SetupAsync(client);
        var key = Guid.NewGuid(); var first = await Created(client, "masters/tags", new { name = "同じ要求" }, key);
        var secondSpace = await Created(client, "collections", new { name = "B" }); var collectionId = secondSpace.GetProperty("id").GetGuid(); Select(client, collectionId);
        var second = await Created(client, "masters/tags", new { name = "同じ要求" }, key); Assert.That(second.GetProperty("id").GetGuid(), Is.Not.EqualTo(first.GetProperty("id").GetGuid()));
        var category = await Created(client, "masters/categories", new { name = "Bカテゴリ", parentId = (Guid?)null, sortOrder = 0 });
        await using var scope = app.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); db.CollectionId = CollectionSchema.DefaultId;
        db.Add(new Product { Id = Guid.NewGuid(), Name = "境界検査", NormalizedName = "境界検査", CategoryId = category.GetProperty("id").GetGuid(), CreatedAt = app.Clock.GetUtcNow(), UpdatedAt = app.Clock.GetUtcNow() });
        Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Test]
    public async Task RenameRequiresManagerAndVersionAndReplayDoesNotRenameAgain()
    {
        await using var app = new TestApplication(); using var client = Client(app); await SetupAsync(client);
        var created = await Created(client, "collections", new { name = "元の名前" }); var id = created.GetProperty("id").GetGuid(); var etag = created.GetProperty("etag").GetString(); var key = Guid.NewGuid();
        var response = await Write(client, "collections/" + id, new { name = "新しい名前" }, HttpMethod.Put, etag, key); Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK)); var renamed = await response.Content.ReadFromJsonAsync<JsonElement>();
        response = await Write(client, "collections/" + id, new { name = "新しい名前" }, HttpMethod.Put, etag, key); Assert.That((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("revision").GetString(), Is.EqualTo(renamed.GetProperty("revision").GetString()));
        Assert.That((await Write(client, "collections/" + id, new { name = "古い更新" }, HttpMethod.Put, etag)).StatusCode, Is.EqualTo(HttpStatusCode.PreconditionFailed));
        Assert.That((await Write(client, "collections", new { name = "不正コピー", importFrom = Guid.NewGuid() })).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task ImagesAndUploadsCannotBeReadOrConsumedFromAnotherCollection()
    {
        await using var app = new TestApplication(); using var client = Client(app); await SetupAsync(client); var uploadId = Guid.NewGuid(); var assetId = Guid.NewGuid();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var now = app.Clock.GetUtcNow();
            var file = new ImageFile { Id = Guid.NewGuid(), Sha256 = new string('a', 64), RelativePath = "objects/aa/" + new string('a', 64), MediaType = "image/webp", ByteLength = 1, Width = 1, Height = 1, CreatedAt = now, UpdatedAt = now }; db.Add(file);
            db.Add(new ImageAsset { Id = assetId, SourceSha256 = new string('b', 64), PolicyHash = new string('c', 64), PolicyJson = "{}", DisplayFileId = file.Id, ThumbnailFileId = file.Id, CreatedAt = now });
            db.Add(new TemporaryUpload { Id = uploadId, UserId = (await db.Users.SingleAsync()).Id, ClientUploadKey = Guid.NewGuid(), State = "ready", ImageAssetId = assetId, SourceSha256 = new string('b', 64), PolicyHash = new string('c', 64), PolicyJson = "{}", ByteLength = 1, CreatedAt = now, UpdatedAt = now, ExpiresAt = now.AddDays(1), ReplayUntil = now.AddDays(7) }); await db.SaveChangesAsync();
        }
        var second = await Created(client, "collections", new { name = "画像の境界" }); var id = second.GetProperty("id").GetGuid(); Select(client, id);
        Assert.That((await client.GetAsync($"/api/v1/temporary-uploads/{uploadId}")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await client.GetAsync($"/api/v1/temporary-uploads/{uploadId}/preview?collectionId={id}")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await client.GetAsync($"/media/assets/{assetId}/display?collectionId={id}")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await Write(client, $"temporary-uploads/{uploadId}", new { }, HttpMethod.Delete)).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task DatabaseAllowsMoreUsersButHttpSetupStaysClosedAndAdministratorRightsAreNotMembershipBypass()
    {
        await using var app = new TestApplication(); using var owner = Client(app); await SetupAsync(owner);
        var other = new AppUser { Id = Guid.NewGuid(), UserName = "editor", LockoutEnabled = true };
        await using (var scope = app.Services.CreateAsyncScope()) Assert.That((await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().CreateAsync(other, Password)).Succeeded, Is.True);
        using var editor = Client(app); Assert.That((await SendAsync(editor, "auth/password", new { loginId = "editor", password = Password })).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await editor.GetAsync("/api/v1/products")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound)); Assert.That((await editor.GetAsync("/api/v1/backups")).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden)); Assert.That((await SetupAsync(editor)).StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        await using (var scope = app.Services.CreateAsyncScope()) { var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); db.Add(new CollectionMember { CollectionId = CollectionSchema.DefaultId, UserId = other.Id, Role = "editor" }); await db.SaveChangesAsync(); }
        Assert.That((await editor.GetAsync("/api/v1/categories/tree")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await Write(editor, $"collections/{CollectionSchema.DefaultId}", new { name = "勝手に変更" }, HttpMethod.Put, "anything")).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }
    [Test]
    public async Task ForeignProductMutationsDuplicatesAndMasterMaintenanceStayIsolated()
    {
        await using var app = new TestApplication(); using var client = Client(app); await SetupAsync(client);
        var category = await Created(client, "masters/categories", new { name = "分類", parentId = (Guid?)null, sortOrder = 0 });
        var created = await Created(client, "products", ProductBody(category.GetProperty("id").GetGuid())); var product = created.GetProperty("product"); var id = product.GetProperty("id").GetGuid();
        var tag = await Created(client, "masters/tags", new { name = "元のタグ" });
        var second = await Created(client, "collections", new { name = "別の操作先" }); Select(client, second.GetProperty("id").GetGuid());
        foreach (var path in new[] { $"products/{id}", $"products/{id}/inventory", $"products/{id}/images", $"trash/products/{id}" }) Assert.That((await client.GetAsync("/api/v1/" + path)).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        foreach (var (path, method) in new[] { ($"products/{id}", HttpMethod.Put), ($"products/{id}", HttpMethod.Delete), ($"products/{id}/inventory-commands", HttpMethod.Post), ($"products/{id}/images", HttpMethod.Put), ($"trash/products/{id}/restore", HttpMethod.Post) }) Assert.That((await Write(client, path, new { }, method, product.GetProperty("etag").GetString())).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        var check = await Write(client, "duplicate-checks", new { input = new { name = "分離テスト", workIds = Array.Empty<Guid>(), identifiers = Array.Empty<object>() } });
        Assert.That(check.StatusCode, Is.EqualTo(HttpStatusCode.OK)); Assert.That((await check.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("candidates").GetArrayLength(), Is.Zero);
        Assert.That((await Write(client, $"masters/tags/{tag.GetProperty("id").GetGuid()}/deletion-preview", new { replacementId = (Guid?)null })).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Select(client, CollectionSchema.DefaultId); Assert.That((await client.GetFromJsonAsync<JsonElement>($"/api/v1/products/{id}")).GetProperty("revision").GetString(), Is.EqualTo(product.GetProperty("revision").GetString()));
    }

    [Test]
    public async Task QueuedRequestsCannotRunUnderAnotherAccountAndRecoveryTargetsOnlyOneUser()
    {
        await using var app = new TestApplication(); using var owner = Client(app); await SetupAsync(owner);
        Guid ownerId; var other = new AppUser { Id = Guid.NewGuid(), UserName = "second.owner", LockoutEnabled = true }; var privateCollection = Guid.NewGuid();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); ownerId = (await db.Users.SingleAsync()).Id;
            Assert.That((await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().CreateAsync(other, Password)).Succeeded, Is.True);
            db.Add(new CollectionSpace { Id = privateCollection, Name = "別の利用者だけ", CreatedAt = app.Clock.GetUtcNow(), UpdatedAt = app.Clock.GetUtcNow() }); db.Add(new CollectionMember { CollectionId = privateCollection, UserId = other.Id }); await db.SaveChangesAsync();
        }
        Select(owner, privateCollection); Assert.That((await owner.GetAsync("/api/v1/categories/tree")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        using var second = Client(app); await SendAsync(second, "auth/password", new { loginId = "second.owner", password = Password });
        second.DefaultRequestHeaders.Add("X-Account-Id", ownerId.ToString());
        Assert.That((await second.GetAsync("/api/v1/collections")).StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        second.DefaultRequestHeaders.Remove("X-Account-Id");
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var recovery = scope.ServiceProvider.GetRequiredService<AccountRecovery>();
            Assert.That((await recovery.ResetAsync("new-local-password", false)).Succeeded, Is.False);
            Assert.That((await recovery.ResetAsync("new-local-password", false, "owner")).Succeeded, Is.True);
        }
        Assert.That((await owner.GetAsync("/api/v1/auth/session")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That((await second.GetAsync("/api/v1/auth/session")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await SendAsync(second, "auth/password", new { loginId = "second.owner", password = Password })).StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task CollectionCreationCanReuseExpiredReplayKeyWithoutTrackingConflict()
    {
        await using var app = new TestApplication(); using var client = Client(app); await SetupAsync(client); var key = Guid.NewGuid();
        var first = await Created(client, "collections", new { name = "一回目" }, key);
        app.Clock.Advance(TimeSpan.FromDays(7));
        await SendAsync(client, "auth/password", new { loginId = "owner", password = Password });
        var second = await Created(client, "collections", new { name = "二回目" }, key);
        Assert.That(second.GetProperty("id").GetGuid(), Is.Not.EqualTo(first.GetProperty("id").GetGuid()));
    }

}
