using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using GoodsManager.Web.Products;
using GoodsManager.Web.Storage;
using Microsoft.EntityFrameworkCore;
namespace GoodsManager.Tests;

public sealed partial class RegistrationTests
{
    private async Task<HttpResponseMessage> Put(Guid id, object draft, string etag, object? confirmation = null, Guid? key = null)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf"); using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/products/{id}") { Content = JsonContent.Create(new { product = draft, duplicateConfirmation = confirmation }) }; request.Headers.Add("X-CSRF-Token", csrf.GetProperty("token").GetString()); request.Headers.Add("Idempotency-Key", (key ?? Guid.NewGuid()).ToString()); request.Headers.TryAddWithoutValidation("If-Match", etag); return await client.SendAsync(request);
    }
    [Test]
    public async Task EditingAllAttributesPreservesHoldingsAndStableUnchangedLinksAndCodes()
    {
        var p = await Fixture(works: [a], codes: [new { scheme = "isbn13", value = "9780306406157" }]); var id = p.GetProperty("id").GetGuid(); var original = p.GetProperty("identifiers")[0].GetProperty("id").GetGuid(); var link = p.GetProperty("links")[0].GetProperty("id").GetGuid(); var revision = await Revision();
        var draft = JsonSerializer.SerializeToNode(Draft("編集名", [a, b], [new { scheme = "isbn13", value = "９７８-０３０６４０６１５７" }]))!.AsObject(); draft["memo"] = "更新した商品メモ"; draft["version"] = null;
        var updated = await Json(await Put(id, draft, p.GetProperty("etag").GetString()!)); Assert.That(updated.GetProperty("name").GetString(), Is.EqualTo("編集名")); Assert.That(updated.GetProperty("version").ValueKind, Is.EqualTo(JsonValueKind.Null)); Assert.That(updated.GetProperty("totalQuantity").GetInt64(), Is.EqualTo(2)); Assert.That(updated.GetProperty("identifiers")[0].GetProperty("id").GetGuid(), Is.EqualTo(original)); Assert.That(updated.GetProperty("links")[0].GetProperty("id").GetGuid(), Is.EqualTo(link)); Assert.That(updated.GetProperty("inventory").GetProperty("lots")[0].GetProperty("memo").GetString(), Is.EqualTo("所持のメモ")); Assert.That(await Revision(), Is.EqualTo(revision + 1));
        var unchanged = await Json(await Put(id, draft, updated.GetProperty("etag").GetString()!)); Assert.That(unchanged.GetProperty("revision").GetString(), Is.EqualTo("2")); Assert.That(await Revision(), Is.EqualTo(revision + 1));
    }
    [Test]
    public async Task EditRequiresOnlyNewStrongCandidatesAndExcludesTheEditedProduct()
    {
        var p = await Fixture("変更前", [a]); var id = p.GetProperty("id").GetGuid(); await Fixture("星のスタンド", [a]); var rejected = await Json(await Put(id, Draft(works: [a]), p.GetProperty("etag").GetString()!), HttpStatusCode.Conflict); var check = rejected.GetProperty("details"); Assert.That(check.GetProperty("strongCandidateCount").GetInt32(), Is.EqualTo(1)); Assert.That(check.GetProperty("candidates")[0].GetProperty("product").GetProperty("id").GetGuid(), Is.Not.EqualTo(id));
        var updated = await Json(await Put(id, Draft(works: [a]), p.GetProperty("etag").GetString()!, Confirmation(check, "update", id))); Assert.That(updated.GetProperty("name").GetString(), Is.EqualTo("星のスタンド"));
        // 既に一致している候補を、版・メモ・参考リンクだけの変更で再確認しない。
        var changed = JsonSerializer.SerializeToNode(Draft(works: [a]))!.AsObject(); changed["memo"] = "補足"; changed["links"] = JsonSerializer.SerializeToNode(new[] { new { url = "https://127.0.0.1:1/no-fetch" } }); Assert.That((await Put(id, changed, updated.GetProperty("etag").GetString()!)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }
    [Test]
    public async Task ProductEditAndInventoryShareTheParentRevisionAndSuccessReplay()
    {
        var p = await Fixture(works: [a]); var id = p.GetProperty("id").GetGuid(); var etag = p.GetProperty("etag").GetString()!; var key = Guid.NewGuid(); var edited = await Json(await Put(id, Draft("更新", [a]), etag, key: key)); var lot = p.GetProperty("inventory").GetProperty("lots")[0].GetProperty("id").GetString(); var command = new { commands = new[] { new { type = "updateLot", lotRef = lot, quantity = 7 } } };
        Assert.That((await Post($"products/{id}/inventory-commands", command, etag: etag)).StatusCode, Is.EqualTo(HttpStatusCode.PreconditionFailed)); var inventory = await Json(await Post($"products/{id}/inventory-commands", command, etag: edited.GetProperty("etag").GetString())); Assert.That(inventory.GetProperty("totalQuantity").GetInt64(), Is.EqualTo(7));
        Assert.That((await Put(id, Draft("別更新", [a]), edited.GetProperty("etag").GetString()!)).StatusCode, Is.EqualTo(HttpStatusCode.PreconditionFailed)); Assert.That((await Json(await Put(id, Draft("更新", [a]), etag, key: key))).ToString(), Is.EqualTo(edited.ToString()));
    }
    [Test]
    public async Task ProductEditFinalFailureAndInvalidReferenceLeaveAllDataUnchanged()
    {
        var p = await Fixture(works: [a]); var id = p.GetProperty("id").GetGuid(); var etag = p.GetProperty("etag").GetString()!; var revision = await Revision(); Assert.That((await Put(id, Draft("変更", [Guid.NewGuid()]), etag)).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest)); await Db(async db => await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_edit BEFORE INSERT ON IdempotencyRecords BEGIN SELECT RAISE(ABORT,'test'); END")); Assert.That((await Put(id, Draft("変更", [b]), etag)).StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
        var latest = await client.GetFromJsonAsync<JsonElement>($"/api/v1/products/{id}"); Assert.That(JsonNode.DeepEquals(JsonNode.Parse(latest.GetRawText()), JsonNode.Parse(p.GetRawText())), Is.True); Assert.That(await Revision(), Is.EqualTo(revision));
    }
    [Test]
    public async Task ImageEditAndInventoryLimitNewUploadsAcrossTheWholeRequest()
    {
        var p = await Fixture(); var id = p.GetProperty("id").GetGuid(); var csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf"); object[] Entries(int count) => Enumerable.Range(0, count).Select(i => (object)new { clientRef = "photo-" + i, temporaryUploadId = Guid.NewGuid(), sortOrder = i }).ToArray();
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/products/{id}/images") { Content = JsonContent.Create(new { entries = Entries(101), primaryRef = (string?)null }) }; request.Headers.Add("X-CSRF-Token", csrf.GetProperty("token").GetString()); request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString()); request.Headers.TryAddWithoutValidation("If-Match", p.GetProperty("etag").GetString()); Assert.That((await client.SendAsync(request)).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        var body = new { commands = new[] { new { type = "replaceItemImages", itemRef = Guid.NewGuid().ToString(), entries = Entries(51) }, new { type = "replaceItemImages", itemRef = Guid.NewGuid().ToString(), entries = Entries(50) } } }; Assert.That((await Post($"products/{id}/inventory-commands", body, etag: p.GetProperty("etag").GetString())).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }
    [Test]
    public async Task EditConfirmationRejectsNewCandidatesAddedAfterThePreview()
    {
        var p = await Fixture("編集前", [a]); var id = p.GetProperty("id").GetGuid(); await Fixture("編集後", [a]); var check = await CheckCore(new { input = Input("編集後", [a]), excludeProductId = id }); var confirmation = Confirmation(check, "update", id); await Fixture("編集後", [a]);
        var result = await Json(await Put(id, Draft("編集後", [a]), p.GetProperty("etag").GetString()!, confirmation), HttpStatusCode.Conflict); Assert.That(result.GetProperty("details").GetProperty("strongCandidateCount").GetInt32(), Is.EqualTo(2)); var latest = await client.GetFromJsonAsync<JsonElement>($"/api/v1/products/{id}"); Assert.That(latest.GetProperty("name").GetString(), Is.EqualTo("編集前")); Assert.That(latest.GetProperty("revision").GetString(), Is.EqualTo("1"));
    }
    [Test]
    public async Task ConcurrentProductAndImageEditsHaveOnlyOneWinnerAndNoPartialChanges()
    {
        var p = await Fixture("編集前", [a]); var id = p.GetProperty("id").GetGuid(); var upload = await Upload(); var before = await Revision();
        async Task<HttpResponseMessage> ImageEdit() { var csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf"); using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/products/{id}/images") { Content = JsonContent.Create(new { entries = new[] { new { clientRef = "photo", temporaryUploadId = upload, sortOrder = 0 } }, primaryRef = "photo" }) }; request.Headers.Add("X-CSRF-Token", csrf.GetProperty("token").GetString()); request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString()); request.Headers.TryAddWithoutValidation("If-Match", p.GetProperty("etag").GetString()); return await client.SendAsync(request); }
        var results = await Task.WhenAll(Put(id, Draft("編集後", [a]), p.GetProperty("etag").GetString()!), ImageEdit()); Assert.That(results.Select(x => x.StatusCode), Is.EquivalentTo(new[] { HttpStatusCode.OK, HttpStatusCode.PreconditionFailed })); var latest = await client.GetFromJsonAsync<JsonElement>($"/api/v1/products/{id}"); Assert.That(latest.GetProperty("revision").GetString(), Is.EqualTo("2")); Assert.That(latest.GetProperty("totalQuantity").GetInt64(), Is.EqualTo(2)); Assert.That(latest.GetProperty("images").GetArrayLength(), Is.EqualTo(results[1].StatusCode == HttpStatusCode.OK ? 1 : 0)); Assert.That(latest.GetProperty("name").GetString(), Is.EqualTo(results[0].StatusCode == HttpStatusCode.OK ? "編集後" : "編集前")); Assert.That(await Revision(), Is.EqualTo(before + 1));
    }
}
