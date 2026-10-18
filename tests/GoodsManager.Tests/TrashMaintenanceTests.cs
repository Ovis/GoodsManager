using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using GoodsManager.Web.Images;
using GoodsManager.Web.Masters;
using GoodsManager.Web.Products;
using Microsoft.EntityFrameworkCore;
namespace GoodsManager.Tests;

public sealed partial class RegistrationTests
{
    /// <summary>削除の本文・条件・確認対象を指定し、同じキーの再送も検証可能にする</summary>
    private async Task<HttpResponseMessage> Delete(string path, string? etag, Guid? key = null, string? confirmation = null)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf"); using var request = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/" + path) { Content = JsonContent.Create(new { }) }; request.Headers.Add("X-CSRF-Token", csrf.GetProperty("token").GetString()); request.Headers.Add("Idempotency-Key", (key ?? Guid.NewGuid()).ToString()); if (etag is not null) request.Headers.TryAddWithoutValidation("If-Match", etag); if (confirmation is not null) request.Headers.Add("X-Confirm-Delete", confirmation); return await client.SendAsync(request);
    }

    [Test]
    public async Task TrashPreservesAllHoldingsImagesAndCodesAndReplayHasNoBody()
    {
        var upload = await Upload(); var p = (await Json(await Post("products", Body(Draft(codes: [new { scheme = "isbn13", value = "9780306406157" }]), uploads: [upload])), HttpStatusCode.Created)).GetProperty("product"); var id = p.GetProperty("id").GetGuid(); var key = Guid.NewGuid(); var revision = await Revision();
        var first = await Delete($"products/{id}", p.GetProperty("etag").GetString(), key); Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.NoContent)); Assert.That(await first.Content.ReadAsStringAsync(), Is.Empty);
        Assert.That((await client.GetAsync($"/api/v1/products/{id}")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound)); var trash = await client.GetFromJsonAsync<JsonElement>($"/api/v1/trash/products/{id}"); Assert.That(trash.GetProperty("totalQuantity").GetInt64(), Is.EqualTo(2)); Assert.That(trash.GetProperty("identifiers").GetArrayLength(), Is.EqualTo(1)); Assert.That(trash.GetProperty("images").GetArrayLength(), Is.EqualTo(1)); Assert.That(trash.GetProperty("deletedAt").ValueKind, Is.EqualTo(JsonValueKind.String));
        var replay = await Delete($"products/{id}", p.GetProperty("etag").GetString(), key); Assert.That(replay.StatusCode, Is.EqualTo(HttpStatusCode.NoContent)); Assert.That(await replay.Content.ReadAsStringAsync(), Is.Empty); Assert.That(await Revision(), Is.EqualTo(revision + 1)); Assert.That(await Db(db => db.Set<ProductChange>().CountAsync(x => x.ProductId == id && x.Kind == "trashed")), Is.EqualTo(1));
        Assert.That((await client.GetFromJsonAsync<JsonElement>("/api/v1/trash/products?q=9780306406157&pageSize=25")).GetProperty("totalCount").GetInt64(), Is.EqualTo(1)); Assert.That((await client.GetFromJsonAsync<JsonElement>("/api/v1/products?inventory=all&q=9780306406157")).GetProperty("totalCount").GetInt64(), Is.Zero);
    }

    [Test]
    public async Task RestoreExcludesItselfAndRequiresCurrentStrongCandidateConfirmation()
    {
        var p = await Fixture(works: [a]); var id = p.GetProperty("id").GetGuid(); await Delete($"products/{id}", p.GetProperty("etag").GetString()); var trash = await client.GetFromJsonAsync<JsonElement>($"/api/v1/trash/products/{id}");
        await Fixture(works: [a]); var rejected = await Json(await Post($"trash/products/{id}/restore", new { }, etag: trash.GetProperty("etag").GetString()), HttpStatusCode.Conflict); var check = rejected.GetProperty("details"); Assert.That(check.GetProperty("strongCandidateCount").GetInt32(), Is.EqualTo(1)); Assert.That(check.GetProperty("candidates")[0].GetProperty("product").GetProperty("id").GetGuid(), Is.Not.EqualTo(id));
        await Fixture(works: [a]); Assert.That((await Post($"trash/products/{id}/restore", new { duplicateConfirmation = Confirmation(check, "restore", id) }, etag: trash.GetProperty("etag").GetString())).StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        var latest = (await Json(await Post($"trash/products/{id}/restore", new { }, etag: trash.GetProperty("etag").GetString()), HttpStatusCode.Conflict)).GetProperty("details"); var restored = await Json(await Post($"trash/products/{id}/restore", new { duplicateConfirmation = Confirmation(latest, "restore", id) }, etag: trash.GetProperty("etag").GetString())); Assert.That(restored.GetProperty("deletedAt").ValueKind, Is.EqualTo(JsonValueKind.Null)); Assert.That(restored.GetProperty("totalQuantity").GetInt64(), Is.EqualTo(2));
    }

    [Test]
    public async Task PurgeRequiresVersionAndExplicitTargetProtectsSharedImageAndKeepsChangeRecord()
    {
        var p = (await Json(await Post("products", Body(Draft("削除対象"), uploads: [await Upload()])), HttpStatusCode.Created)).GetProperty("product"); var other = (await Json(await Post("products", Body(Draft("共有先"), uploads: [await Upload()])), HttpStatusCode.Created)).GetProperty("product"); var id = p.GetProperty("id").GetGuid(); Assert.That(other.GetProperty("images")[0].GetProperty("assetId").GetGuid(), Is.EqualTo(p.GetProperty("images")[0].GetProperty("assetId").GetGuid())); await Delete($"products/{id}", p.GetProperty("etag").GetString()); var trash = await client.GetFromJsonAsync<JsonElement>($"/api/v1/trash/products/{id}"); var etag = trash.GetProperty("etag").GetString(); var before = await Revision();
        Assert.That((await Delete($"trash/products/{id}", etag)).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest)); Assert.That((await Delete($"trash/products/{id}", null, confirmation: id.ToString())).StatusCode, Is.EqualTo((HttpStatusCode)428)); Assert.That((await Delete($"trash/products/{id}", p.GetProperty("etag").GetString(), confirmation: id.ToString())).StatusCode, Is.EqualTo(HttpStatusCode.PreconditionFailed));
        var key = Guid.NewGuid(); Assert.That((await Delete($"trash/products/{id}", etag, key, id.ToString())).StatusCode, Is.EqualTo(HttpStatusCode.NoContent)); Assert.That((await Delete($"trash/products/{id}", etag, key, id.ToString())).StatusCode, Is.EqualTo(HttpStatusCode.NoContent)); Assert.That((await Delete($"trash/products/{id}", etag, key, Guid.NewGuid().ToString())).StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(await Db(db => db.Set<Product>().AnyAsync(x => x.Id == id)), Is.False); Assert.That(await Db(db => db.Set<ProductChange>().CountAsync(x => x.ProductId == id && x.Kind == "purged")), Is.EqualTo(1)); Assert.That(await Revision(), Is.EqualTo(before + 1)); Assert.That((await client.GetAsync(other.GetProperty("images")[0].GetProperty("displayUrl").GetString())).StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task PurgeFinalFailureRollsBackCascadesAndChanges()
    {
        var p = await Fixture(); var id = p.GetProperty("id").GetGuid(); await Delete($"products/{id}", p.GetProperty("etag").GetString()); var trash = await client.GetFromJsonAsync<JsonElement>($"/api/v1/trash/products/{id}"); var before = await Revision(); await Db(async db => { await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_purge BEFORE INSERT ON IdempotencyRecords BEGIN SELECT RAISE(ABORT,'test'); END"); return 0; }); Assert.That((await Delete($"trash/products/{id}", trash.GetProperty("etag").GetString(), confirmation: id.ToString())).StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError)); var latest = await client.GetFromJsonAsync<JsonElement>($"/api/v1/trash/products/{id}"); Assert.That(JsonNode.DeepEquals(JsonNode.Parse(latest.GetRawText()), JsonNode.Parse(trash.GetRawText())), Is.True); Assert.That(await Revision(), Is.EqualTo(before)); Assert.That(await Db(db => db.Set<ProductChange>().AnyAsync(x => x.Kind == "purged")), Is.False);
    }

}
