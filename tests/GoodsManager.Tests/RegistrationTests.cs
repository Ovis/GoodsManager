using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoodsManager.Web.Masters;
using GoodsManager.Web.Products;
using GoodsManager.Web.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static GoodsManager.Tests.AuthenticationTests;
namespace GoodsManager.Tests;

public sealed class RegistrationTests
{
    private TestApplication app = null!; private HttpClient client = null!; private Guid category; private Guid a, b, c;
    [SetUp]
    public async Task Start()
    {
        app = new(); client = Client(app); await SetupAsync(client);
        await Db(async db => { category = await db.Set<Category>().Select(x => x.Id).FirstAsync(); var now = app.Clock.GetUtcNow(); foreach (var name in new[] { "A", "B", "C" }) db.Add(new Work { Id = Guid.NewGuid(), Name = name, NormalizedName = name.ToLowerInvariant(), CreatedAt = now, UpdatedAt = now }); await db.SaveChangesAsync(); var works = await db.Set<Work>().OrderBy(x => x.Name).ToArrayAsync(); (a, b, c) = (works[0].Id, works[1].Id, works[2].Id); return 0; });
    }
    [TearDown] public async Task Stop() { client.Dispose(); await app.DisposeAsync(); }
    private async Task<T> Db<T>(Func<AppDbContext, Task<T>> action) { await using var scope = app.Services.CreateAsyncScope(); return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>()); }
    private Task<long> Revision() => Db(async db => (await db.SystemStates.AsNoTracking().SingleAsync()).DataRevision);
    private static object Fields() => new { condition = "unopened", storageLocation = "棚", purchasePlace = "会場", memo = "所持のメモ" };
    private object Draft(string name = "星のスタンド", Guid[]? works = null, object[]? codes = null) => new { name, categoryId = category, version = "版", workIds = works ?? [], characterIds = Array.Empty<Guid>(), tagIds = Array.Empty<Guid>(), identifiers = codes ?? [], links = new[] { new { url = "https://example.com/reference" } }, memo = "商品メモ" };
    private object Input(string name = "星のスタンド", Guid[]? works = null, object[]? codes = null) => new { name, workIds = works ?? [], identifiers = codes ?? [], categoryId = category, characterIds = Array.Empty<Guid>() };
    private object Body(object draft, object? confirmation = null, Guid[]? uploads = null) => new { product = draft, initialLot = new { quantity = 2, fields = Fields() }, images = (uploads ?? []).Select(id => new { temporaryUploadId = id }), duplicateConfirmation = confirmation };
    private async Task<HttpResponseMessage> Post(string path, object value, Guid? key = null, string? etag = null, HttpClient? sender = null)
    {
        sender ??= client; var csrf = await sender.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf"); using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/" + path) { Content = JsonContent.Create(value) }; request.Headers.Add("X-CSRF-Token", csrf.GetProperty("token").GetString()); request.Headers.Add("Idempotency-Key", (key ?? Guid.NewGuid()).ToString()); if (etag is not null) request.Headers.TryAddWithoutValidation("If-Match", etag); return await sender.SendAsync(request);
    }
    private static async Task<JsonElement> Json(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK) { Assert.That(response.StatusCode, Is.EqualTo(expected), await response.Content.ReadAsStringAsync()); return await response.Content.ReadFromJsonAsync<JsonElement>(); }
    private async Task<JsonElement> Fixture(string name = "星のスタンド", Guid[]? works = null, object[]? codes = null) => await Json(await Post("dev/products", new { product = Draft(name, works, codes), initialLot = new { quantity = 2, fields = Fields() } }), HttpStatusCode.Created);
    private Task<JsonElement> Check(object input) => CheckCore(new { input });
    private async Task<JsonElement> CheckCore(object body) => await Json(await Post("duplicate-checks", body));
    private static object Confirmation(JsonElement check, string decision = "createSeparate", Guid? target = null) => new { inputFingerprint = check.GetProperty("inputFingerprint").GetString(), confirmedCandidates = check.GetProperty("candidates").EnumerateArray().Where(x => x.GetProperty("strength").GetString() == "strong").Select(x => new { productId = x.GetProperty("product").GetProperty("id").GetGuid(), reasons = x.GetProperty("reasons").EnumerateArray().Select(r => r.GetString()).ToArray() }), decision, targetProductId = target };

    [Test]
    public async Task NameMatchingUsesAnyCommonWorkAndIgnoresOrderAndDuplicateWorkIds()
    {
        await Fixture(works: [a, b]);
        foreach (var works in new[] { new[] { b, c }, new[] { c, b, b } }) { var result = await Check(Input(works: works)); Assert.That(result.GetProperty("strongCandidateCount").GetInt32(), Is.EqualTo(1)); Assert.That(result.GetProperty("candidates")[0].GetProperty("reasons").ToString(), Does.Contain("NAME_WORK_EXACT")); }
        var first = await Check(Input(works: [b, c])); var reordered = await Check(Input(works: [c, b, b])); Assert.That(first.GetProperty("inputFingerprint").GetString(), Is.EqualTo(reordered.GetProperty("inputFingerprint").GetString()));
        foreach (var works in new[] { Array.Empty<Guid>(), new[] { c } }) Assert.That((await Check(Input(works: works))).GetProperty("strongCandidateCount").GetInt32(), Is.Zero);
        Assert.That((await Check(Input("星の", [a]))).GetProperty("strongCandidateCount").GetInt32(), Is.Zero);
    }
    [Test]
    public async Task EquivalentCodesMatchIndependentlyOfNameAndCountTheProductOnce()
    {
        await Fixture("別名", codes: [new { scheme = "isbn10", value = "0-306-40615-2" }, new { scheme = "isbn13", value = "9780306406157" }]);
        var result = await Check(Input("無関係", codes: [new { scheme = "ean13", value = "９７８０３０６４０６１５７" }])); Assert.That(result.GetProperty("strongCandidateCount").GetInt32(), Is.EqualTo(1)); Assert.That(result.GetProperty("candidates")[0].GetProperty("reasons").ToString(), Does.Contain("CODE_EXACT"));
        Assert.That((await Post("duplicate-checks", new { input = Input(codes: [new { scheme = "ean13", value = "123" }]) })).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }
    [Test]
    public async Task StrongCandidatesIncludeZeroAndTrashAndAreNeverTruncated()
    {
        var p = await Fixture(works: [a]); var id = p.GetProperty("id").GetGuid(); await Db(async db => { await db.Set<InventoryLot>().Where(x => x.ProductId == id).ExecuteDeleteAsync(); var product = await db.Set<Product>().SingleAsync(x => x.Id == id); product.DeletedAt = app.Clock.GetUtcNow(); await db.SaveChangesAsync(); return 0; });
        var result = await Check(Input(works: [a])); Assert.That(result.GetProperty("candidates")[0].GetProperty("isTrashed").GetBoolean(), Is.True); Assert.That(result.GetProperty("candidates")[0].GetProperty("product").GetProperty("totalQuantity").GetInt64(), Is.Zero);
        await Db(async db => { var now = app.Clock.GetUtcNow(); for (var i = 0; i < 25; i++) { var product = new Product { Id = Guid.NewGuid(), Name = "星のスタンド", NormalizedName = "星のスタンド", CategoryId = category, CreatedAt = now, UpdatedAt = now }; db.Add(product); db.Add(new ProductWork { ProductId = product.Id, WorkId = a }); } await db.SaveChangesAsync(); return 0; });
        Assert.That((await Check(Input(works: [a]))).GetProperty("strongCandidateCount").GetInt32(), Is.EqualTo(26));
    }
    [Test]
    public async Task SaveRequiresConfirmationAndAtomicallyStoresASeparateProductWithOneRevision()
    {
        await Fixture(works: [a]); var before = await Revision(); var draft = Draft(works: [a]); var rejected = await Json(await Post("products", Body(draft)), HttpStatusCode.Conflict); Assert.That(rejected.GetProperty("code").GetString(), Is.EqualTo("DUPLICATE_CONFIRMATION_REQUIRED")); Assert.That(await Revision(), Is.EqualTo(before)); Assert.That(await Db(db => db.Set<Product>().CountAsync()), Is.EqualTo(1));
        var created = await Json(await Post("products", Body(draft, Confirmation(rejected.GetProperty("details")))), HttpStatusCode.Created); Assert.That(created.GetProperty("outcome").GetString(), Is.EqualTo("created")); Assert.That(created.GetProperty("product").GetProperty("totalQuantity").GetInt64(), Is.EqualTo(2)); Assert.That(await Revision(), Is.EqualTo(before + 1)); Assert.That(await Db(db => db.Set<Product>().CountAsync()), Is.EqualTo(2));
    }
    [Test]
    public async Task AddedCandidateOrReasonRequiresNewConfirmationButRemovedCandidateDoesNot()
    {
        var p = await Fixture(works: [a]); var check = await Check(Input(works: [a])); await Fixture(works: [a]); Assert.That((await Post("products", Body(Draft(works: [a]), Confirmation(check)))).StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        check = await Check(Input(works: [a])); await Db(async db => { var id = p.GetProperty("id").GetGuid(); await db.Set<Product>().Where(x => x.Id == id).ExecuteDeleteAsync(); return 0; }); Assert.That((await Post("products", Body(Draft(works: [a]), Confirmation(check)))).StatusCode, Is.EqualTo(HttpStatusCode.Created));
    }
    [Test]
    public async Task ConcurrentFirstRegistrationsCreateOneProductAndReturnConfirmationForTheOther()
    {
        using var other = Client(app); await SendAsync(other, "auth/password", new { loginId = "owner", password = Password, rememberMe = false });
        var body = Body(Draft(works: [a])); var results = await Task.WhenAll(Post("products", body), Post("products", body, sender: other)); Assert.That(results.Select(x => x.StatusCode), Is.EquivalentTo(new[] { HttpStatusCode.Created, HttpStatusCode.Conflict })); Assert.That(await Db(db => db.Set<Product>().CountAsync()), Is.EqualTo(1));
    }
    [Test]
    public async Task FrozenSuccessReplayDoesNotRunDuplicateChecksAgainOrCreateTwice()
    {
        var key = Guid.NewGuid(); var body = Body(Draft(works: [a])); var response = await Post("products", body, key); var first = await Json(response, HttpStatusCode.Created); var location = response.Headers.Location; await Fixture(works: [a]); var before = await Revision(); var replay = await Post("products", body, key); Assert.That((await Json(replay, HttpStatusCode.Created)).ToString(), Is.EqualTo(first.ToString())); Assert.That(replay.Headers.Location, Is.EqualTo(location)); Assert.That(await Revision(), Is.EqualTo(before)); Assert.That((await Post("products", Body(Draft("変更", [a])), key)).StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }
    [Test]
    public async Task AddToExistingPreservesAllProductFieldsAndRequiresCurrentEtag()
    {
        var p = await Fixture(works: [a]); var id = p.GetProperty("id").GetGuid(); var input = Input(works: [a]); var check = await Check(input); var body = new { input, lot = new { quantity = 3, fields = Fields() }, images = Array.Empty<object>(), duplicateConfirmation = Confirmation(check, "addToExisting", id) }; var before = await Revision(); var key = Guid.NewGuid();
        var result = await Json(await Post($"products/{id}/holdings", body, key, p.GetProperty("etag").GetString())); var updated = result.GetProperty("product"); Assert.That(updated.GetProperty("totalQuantity").GetInt64(), Is.EqualTo(5)); Assert.That(updated.GetProperty("name").GetString(), Is.EqualTo(p.GetProperty("name").GetString())); Assert.That(updated.GetProperty("memo").GetString(), Is.EqualTo("商品メモ")); Assert.That(await Revision(), Is.EqualTo(before + 1));
        Assert.That((await Post($"products/{id}/holdings", body, etag: p.GetProperty("etag").GetString())).StatusCode, Is.EqualTo(HttpStatusCode.PreconditionFailed)); Assert.That((await Json(await Post($"products/{id}/holdings", body, key, p.GetProperty("etag").GetString()))).ToString(), Is.EqualTo(result.ToString()));
    }
    [Test]
    public async Task FinalResponseFailureRollsBackProductLotsRelationsAndRevision()
    {
        var before = await Revision(); await Db(async db => await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_registration BEFORE INSERT ON IdempotencyRecords BEGIN SELECT RAISE(ABORT,'test'); END")); Assert.That((await Post("products", Body(Draft(works: [a])))).StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError)); Assert.That(await Db(db => db.Set<Product>().CountAsync()), Is.Zero); Assert.That(await Db(db => db.Set<InventoryLot>().CountAsync()), Is.Zero); Assert.That(await Db(db => db.Set<ProductWork>().CountAsync()), Is.Zero); Assert.That(await Revision(), Is.EqualTo(before));
    }
    private async Task<Guid> Upload(int width = 640)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf"); using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/temporary-uploads"); request.Headers.Add("X-CSRF-Token", csrf.GetProperty("token").GetString()); request.Headers.Add("Client-Upload-Key", Guid.NewGuid().ToString()); var form = new MultipartFormDataContent(); form.Add(new ByteArrayContent(ImageConversionTests.Png(width)), "file", "photo.png"); request.Content = form;
        var result = await Json(await client.SendAsync(request), HttpStatusCode.Accepted); var id = result.GetProperty("id").GetGuid();
        for (var i = 0; i < 200; i++) { result = await client.GetFromJsonAsync<JsonElement>($"/api/v1/temporary-uploads/{id}"); if (result.GetProperty("state").GetString() is not ("pending" or "processing")) break; await Task.Delay(25); }
        Assert.That(result.GetProperty("state").GetString(), Is.EqualTo("ready")); return id;
    }
    [Test]
    public async Task ImagesAreConsumedWithCreationAndExistingAdditionKeepsPrimaryAndDeduplicates()
    {
        var upload = await Upload(); var before = await Revision(); var result = await Json(await Post("products", Body(Draft(works: [a]), uploads: [upload])), HttpStatusCode.Created); var p = result.GetProperty("product"); var id = p.GetProperty("id").GetGuid(); Assert.That(p.GetProperty("images").GetArrayLength(), Is.EqualTo(1)); Assert.That(await Revision(), Is.EqualTo(before + 1));
        Assert.That(await Db(async db => (await db.Set<GoodsManager.Web.Images.TemporaryUpload>().SingleAsync(x => x.Id == upload)).State), Is.EqualTo("consumed"));
        var same = await Upload(); var input = Input(works: [a]); var check = await Check(input); var add = new { input, lot = new { quantity = 1, fields = Fields() }, images = new[] { new { temporaryUploadId = same } }, duplicateConfirmation = Confirmation(check, "addToExisting", id) };
        var added = await Json(await Post($"products/{id}/holdings", add, etag: p.GetProperty("etag").GetString())); var updated = added.GetProperty("product"); Assert.That(updated.GetProperty("totalQuantity").GetInt64(), Is.EqualTo(3)); Assert.That(updated.GetProperty("images").GetArrayLength(), Is.EqualTo(1)); Assert.That(updated.GetProperty("images")[0].GetProperty("linkId").GetGuid(), Is.EqualTo(p.GetProperty("images")[0].GetProperty("linkId").GetGuid())); Assert.That(updated.GetProperty("images")[0].GetProperty("isPrimary").GetBoolean(), Is.True); Assert.That(added.GetProperty("deduplicatedCount").GetInt32(), Is.EqualTo(1)); Assert.That(await Revision(), Is.EqualTo(before + 2));
    }
    [Test]
    public async Task FinalFailureRollsBackImageConsumptionAndNewProductTogether()
    {
        var upload = await Upload(); var before = await Revision(); await Db(async db => await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_registration_image BEFORE INSERT ON IdempotencyRecords BEGIN SELECT RAISE(ABORT,'test'); END"));
        Assert.That((await Post("products", Body(Draft(), uploads: [upload]))).StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError)); Assert.That(await Db(db => db.Set<Product>().CountAsync()), Is.Zero); Assert.That(await Db(db => db.Set<GoodsManager.Web.Images.ProductImage>().CountAsync()), Is.Zero); Assert.That(await Db(async db => (await db.Set<GoodsManager.Web.Images.TemporaryUpload>().SingleAsync(x => x.Id == upload)).State), Is.EqualTo("ready")); Assert.That(await Revision(), Is.EqualTo(before));
    }
    [Test]
    public async Task NewReasonForTheSameCandidateRequiresConfirmationAgain()
    {
        var p = await Fixture(works: [a]); object[] codes = [new { scheme = "isbn13", value = "9780306406157" }]; var check = await Check(Input(works: [a], codes: codes));
        await Db(async db => { var now = app.Clock.GetUtcNow(); db.Add(new ProductIdentifier { Id = Guid.NewGuid(), ProductId = p.GetProperty("id").GetGuid(), Scheme = "isbn13", RawValue = "9780306406157", NormalizedValue = "9780306406157", ComparisonValue = "09780306406157", CreatedAt = now, UpdatedAt = now }); await db.SaveChangesAsync(); return 0; });
        var response = await Json(await Post("products", Body(Draft(works: [a], codes: codes), Confirmation(check))), HttpStatusCode.Conflict); Assert.That(response.GetProperty("details").GetProperty("candidates")[0].GetProperty("reasons").GetArrayLength(), Is.EqualTo(2));
    }
    [Test]
    public async Task OwnershipAndReferenceChangesDoNotInvalidateConfirmedDuplicateInput()
    {
        await Fixture(works: [a]); var check = await Check(Input(works: [a])); var changed = new { name = "星のスタンド", categoryId = category, version = "他の版", workIds = new[] { a }, characterIds = Array.Empty<Guid>(), tagIds = Array.Empty<Guid>(), identifiers = Array.Empty<object>(), links = new[] { new { url = "https://example.com/changed" } }, memo = "異なるメモ" };
        var body = new { product = changed, initialLot = new { quantity = 7, fields = Fields() }, images = Array.Empty<object>(), duplicateConfirmation = Confirmation(check) }; Assert.That((await Post("products", body)).StatusCode, Is.EqualTo(HttpStatusCode.Created));
    }
    [Test]
    public async Task TrashCannotBeAnAdditionTargetAndChecksNeedAuthenticationAndCsrf()
    {
        var p = await Fixture(works: [a]); var id = p.GetProperty("id").GetGuid(); var input = Input(works: [a]); var check = await Check(input); await Db(async db => { await db.Set<Product>().Where(x => x.Id == id).ExecuteUpdateAsync(x => x.SetProperty(y => y.DeletedAt, app.Clock.GetUtcNow())); return 0; });
        Assert.That((await Post($"products/{id}/holdings", new { input, lot = new { quantity = 1, fields = Fields() }, images = Array.Empty<object>(), duplicateConfirmation = Confirmation(check, "addToExisting", id) }, etag: p.GetProperty("etag").GetString())).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        using var anonymous = Client(app); Assert.That((await anonymous.PostAsJsonAsync("/api/v1/duplicate-checks", new { input })).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized)); Assert.That((await client.PostAsJsonAsync("/api/v1/duplicate-checks", new { input })).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }
    [Test]
    public async Task WeakCandidatesUseApprovedPriorityAndTopTwentyWithoutBlockingSave()
    {
        var character = Guid.NewGuid(); await Db(async db => { var now = app.Clock.GetUtcNow(); db.Add(new Character { Id = character, Name = "キャラ", NormalizedName = "キャラ", CreatedAt = now, UpdatedAt = now }); for (var i = 0; i < 24; i++) { var product = new Product { Id = Guid.NewGuid(), Name = i < 8 ? "星のスタンド" : i < 16 ? "星のスタンド 特典" + i : "別の商品" + i, NormalizedName = i < 8 ? "星のスタンド" : i < 16 ? "星のスタンド 特典" + i : "別の商品" + i, CategoryId = category, CreatedAt = now.AddSeconds(i), UpdatedAt = now }; db.Add(product); if (i >= 16) db.Add(new ProductCharacter { ProductId = product.Id, CharacterId = character }); } await db.SaveChangesAsync(); return 0; });
        var input = new { name = "星のスタンド", workIds = Array.Empty<Guid>(), identifiers = Array.Empty<object>(), categoryId = category, characterIds = new[] { character } }; var result = await Check(input); Assert.That(result.GetProperty("strongCandidateCount").GetInt32(), Is.Zero); Assert.That(result.GetProperty("weakCandidateCount").GetInt32(), Is.EqualTo(24)); var candidates = result.GetProperty("candidates").EnumerateArray().ToArray(); Assert.That(candidates, Has.Length.EqualTo(20)); Assert.That(candidates.Take(8).All(x => x.GetProperty("reasons")[0].GetString() == "NAME_EXACT"), Is.True); Assert.That(candidates.Skip(8).Take(8).All(x => x.GetProperty("reasons")[0].GetString() == "NAME_PARTIAL"), Is.True); Assert.That(candidates.Skip(16).All(x => x.GetProperty("reasons")[0].GetString() == "CATEGORY_CHARACTER"), Is.True); Assert.That(candidates[8].GetProperty("product").GetProperty("name").GetString(), Is.EqualTo("星のスタンド 特典15"));
        Assert.That((await Post("products", Body(Draft()))).StatusCode, Is.EqualTo(HttpStatusCode.Created));
    }
    [Test]
    public async Task RequestArraysAndRawBodyHaveLimitsWithoutLimitingSavedTotals()
    {
        var tooMany = Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToArray(); Assert.That((await Post("duplicate-checks", new { input = Input(works: tooMany) })).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        var draft = JsonSerializer.SerializeToNode(Draft())!.AsObject(); foreach (var field in new[] { "workIds", "characterIds", "tagIds", "identifiers", "links" }) { var changed = draft.DeepClone().AsObject(); changed[field] = field == "identifiers" ? JsonSerializer.SerializeToNode(Enumerable.Range(0, 101).Select(_ => new { scheme = "ean13", value = "9780306406157" })) : field == "links" ? JsonSerializer.SerializeToNode(Enumerable.Range(0, 101).Select(i => new { url = "https://example.com/" + i })) : JsonSerializer.SerializeToNode(tooMany); Assert.That((await Post("products", Body(changed))).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), field); }
        Assert.That((await Post("products", Body(Draft(), uploads: tooMany))).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf"); using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/duplicate-checks") { Content = new StringContent(new string(' ', 262145), System.Text.Encoding.UTF8, "application/json") }; request.Headers.Add("X-CSRF-Token", csrf.GetProperty("token").GetString()); Assert.That((await client.SendAsync(request)).StatusCode, Is.EqualTo(HttpStatusCode.RequestEntityTooLarge));
        var hundred = Enumerable.Repeat(a, 100).ToArray(); Assert.That((await Post("duplicate-checks", new { input = Input(works: hundred) })).StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }
    [Test]
    public async Task ConfirmationCannotBeReusedForChangedDuplicateInputOrWrongDecision()
    {
        await Fixture(works: [a]); var check = await Check(Input(works: [a])); var confirmation = Confirmation(check); Assert.That((await Post("products", Body(Draft("星のスタンド", [a, b]), confirmation))).StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        var id = (await Fixture("追加対象", [b])).GetProperty("id").GetGuid(); Assert.That((await Post("products", Body(Draft(works: [a]), Confirmation(check, "addToExisting", id)))).StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        var rejected = await Json(await Post("products", Body(Draft(works: [a]), new { inputFingerprint = check.GetProperty("inputFingerprint").GetString(), confirmedCandidates = Array.Empty<object>(), decision = "createSeparate", targetProductId = (Guid?)null })), HttpStatusCode.Conflict); Assert.That(rejected.GetProperty("details").GetProperty("strongCandidateCount").GetInt32(), Is.EqualTo(1));
    }
    [Test]
    public async Task AppendingDifferentImagesPreservesExistingLinksPrimaryAndRelativeOrder()
    {
        var first = await Upload(); var created = await Json(await Post("products", Body(Draft(works: [a]), uploads: [first])), HttpStatusCode.Created); var product = created.GetProperty("product"); var id = product.GetProperty("id").GetGuid(); var linkId = product.GetProperty("images")[0].GetProperty("linkId").GetGuid();
        await Db(async db => { await db.Set<GoodsManager.Web.Images.ProductImage>().Where(x => x.ProductId == id).ExecuteUpdateAsync(x => x.SetProperty(y => y.SortOrder, int.MaxValue)); return 0; });
        var second = await Upload(641); var check = await Check(Input(works: [a])); var result = await Json(await Post($"products/{id}/holdings", new { input = Input(works: [a]), lot = new { quantity = 1, fields = Fields() }, images = new[] { new { temporaryUploadId = second } }, duplicateConfirmation = Confirmation(check, "addToExisting", id) }, etag: product.GetProperty("etag").GetString())); var images = result.GetProperty("product").GetProperty("images");
        Assert.That(images.GetArrayLength(), Is.EqualTo(2)); Assert.That(images[0].GetProperty("linkId").GetGuid(), Is.EqualTo(linkId)); Assert.That(images[0].GetProperty("isPrimary").GetBoolean(), Is.True); Assert.That(images[1].GetProperty("isPrimary").GetBoolean(), Is.False); Assert.That(images[1].GetProperty("sortOrder").GetInt32(), Is.GreaterThan(images[0].GetProperty("sortOrder").GetInt32())); Assert.That(result.GetProperty("product").GetProperty("revision").GetString(), Is.EqualTo("2"));
    }
}
