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
    [TestCase("works")]
    [TestCase("series")]
    [TestCase("characters")]
    [TestCase("tags")]
    [TestCase("organizations")]
    public async Task MasterMergeMovesEveryReferenceDeduplicatesAndPreservesTargetAttributes(string kind)
    {
        async Task<JsonElement> Master(string name) => await Json(await Post("masters/" + kind, kind == "organizations" ? new { name, websiteUrl = "https://example.com/" + name, memo = name } : (object)new { name }), HttpStatusCode.Created);
        var source = await Master("元"); var target = await Master("先"); var sid = source.GetProperty("id").GetGuid(); var tid = target.GetProperty("id").GetGuid(); var p = await Fixture("対象", [a]); var pid = p.GetProperty("id").GetGuid();
        await Db(async db => { if (kind == "works") { db.AddRange(new ProductWork { ProductId = pid, WorkId = sid }, new ProductWork { ProductId = pid, WorkId = tid }); db.Add(new CharacterWork { CharacterId = (await db.Set<Character>().FirstOrDefaultAsync())?.Id ?? AddCharacter(db), WorkId = sid }); } else if (kind == "series") db.AddRange(new WorkSeries { WorkId = a, SeriesId = sid }, new WorkSeries { WorkId = a, SeriesId = tid }); else if (kind == "characters") db.AddRange(new ProductCharacter { ProductId = pid, CharacterId = sid }, new ProductCharacter { ProductId = pid, CharacterId = tid }, new CharacterWork { CharacterId = sid, WorkId = a }); else if (kind == "tags") db.AddRange(new ProductTag { ProductId = pid, TagId = sid }, new ProductTag { ProductId = pid, TagId = tid }); else { var product = await db.Set<Product>().SingleAsync(x => x.Id == pid); product.ManufacturerId = sid; product.SellerId = sid; } await db.SaveChangesAsync(); return 0; });
        await Delete($"products/{pid}", p.GetProperty("etag").GetString()); var preview = await Json(await Post($"masters/{kind}/merge-preview", new { sourceId = sid, targetId = tid })); Assert.That(preview.GetProperty("confirmedImpact").GetProperty("trashedProductCount").GetInt64(), Is.EqualTo(1)); var key = Guid.NewGuid(); var body = new { sourceId = sid, targetId = tid, confirmedImpact = preview.GetProperty("confirmedImpact") }; var result = await Json(await Post($"masters/{kind}/merges", body, key)); Assert.That(result.GetProperty("target").GetProperty("name").GetString(), Is.EqualTo("先")); if (kind == "organizations") Assert.That(result.GetProperty("target").GetProperty("memo").GetString(), Is.EqualTo("先")); Assert.That((await client.GetAsync($"/api/v1/masters/{kind}/{sid}")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound)); Assert.That((await Json(await Post($"masters/{kind}/merges", body, key))).ToString(), Is.EqualTo(result.ToString())); var latest = await client.GetFromJsonAsync<JsonElement>($"/api/v1/trash/products/{pid}"); Assert.That(latest.GetProperty("totalQuantity").GetInt64(), Is.EqualTo(2)); Assert.That(latest.GetProperty("revision").GetString(), Is.EqualTo("3"));
    }
    private Guid AddCharacter(GoodsManager.Web.Storage.AppDbContext db) { var character = new Character { Id = Guid.NewGuid(), Name = "キャラ", NormalizedName = "キャラ" }; db.Add(character); return character.Id; }

    [Test]
    public async Task MasterPreviewDetectsSameCountReferenceReplacementAndFinalFailureRollsBack()
    {
        var p = await Fixture("対象", [a]); var pid = p.GetProperty("id").GetGuid(); var preview = await Json(await Post("masters/works/merge-preview", new { sourceId = a, targetId = b })); var other = await Fixture("入替先", [a]); await Db(async db => { await db.Set<ProductWork>().Where(x => x.ProductId == pid && x.WorkId == a).ExecuteDeleteAsync(); return 0; }); var stale = await Json(await Post("masters/works/merges", new { sourceId = a, targetId = b, confirmedImpact = preview.GetProperty("confirmedImpact") }), HttpStatusCode.Conflict); Assert.That(stale.GetProperty("code").GetString(), Is.EqualTo("MASTER_IMPACT_CHANGED")); var latest = stale.GetProperty("details"); var before = await Revision(); await Db(async db => { await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_merge BEFORE INSERT ON IdempotencyRecords BEGIN SELECT RAISE(ABORT,'test'); END"); return 0; }); Assert.That((await Post("masters/works/merges", new { sourceId = a, targetId = b, confirmedImpact = latest.GetProperty("confirmedImpact") })).StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError)); Assert.That(await Db(db => db.Set<ProductWork>().CountAsync(x => x.WorkId == a)), Is.EqualTo(1)); Assert.That(await Db(db => db.Set<Work>().AnyAsync(x => x.Id == a)), Is.True); Assert.That(await Revision(), Is.EqualTo(before));
    }

    [Test]
    public async Task CategoryDeletionRejectsTrashAndChildrenThenDeletesOnlyWhenEmpty()
    {
        var parent = await Json(await Post("masters/categories", new { name = "親", parentId = (Guid?)null, sortOrder = 0 }), HttpStatusCode.Created); var id = parent.GetProperty("id").GetGuid(); var child = await Json(await Post("masters/categories", new { name = "子", parentId = id, sortOrder = 1 }), HttpStatusCode.Created); var preview = await Json(await Post($"masters/categories/{id}/deletion-preview", new { })); Assert.That(preview.GetProperty("canDelete").GetBoolean(), Is.False); Assert.That((await Post($"masters/categories/{id}/deletion", new { confirmedImpact = preview.GetProperty("confirmedImpact"), detachConfirmed = true }, etag: parent.GetProperty("etag").GetString())).StatusCode, Is.EqualTo(HttpStatusCode.Conflict)); var cid = child.GetProperty("id").GetGuid(); var childPreview = await Json(await Post($"masters/categories/{cid}/deletion-preview", new { })); Assert.That((await Post($"masters/categories/{cid}/deletion", new { confirmedImpact = childPreview.GetProperty("confirmedImpact") }, etag: child.GetProperty("etag").GetString())).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        var p = await Fixture(); await Db(async db => { var product = await db.Set<Product>().SingleAsync(); product.CategoryId = id; product.DeletedAt = app.Clock.GetUtcNow(); await db.SaveChangesAsync(); return 0; }); preview = await Json(await Post($"masters/categories/{id}/deletion-preview", new { })); Assert.That(preview.GetProperty("confirmedImpact").GetProperty("trashedProductCount").GetInt64(), Is.EqualTo(1)); Assert.That((await Post($"masters/categories/{id}/deletion", new { confirmedImpact = preview.GetProperty("confirmedImpact") }, etag: parent.GetProperty("etag").GetString())).StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task UsedOrganizationDeletionRequiresDetachOrReplacementAndPreservesTheProduct(bool replace)
    {
        var source = await Json(await Post("masters/organizations", new { name = "元" }), HttpStatusCode.Created); var target = await Json(await Post("masters/organizations", new { name = "先" }), HttpStatusCode.Created); var sid = source.GetProperty("id").GetGuid(); Guid? tid = replace ? target.GetProperty("id").GetGuid() : null; var p = await Fixture(); var pid = p.GetProperty("id").GetGuid(); await Db(async db => { var product = await db.Set<Product>().SingleAsync(); product.ManufacturerId = sid; product.SellerId = sid; await db.SaveChangesAsync(); return 0; }); var preview = await Json(await Post($"masters/organizations/{sid}/deletion-preview", new { replacementId = tid })); if (!replace) Assert.That((await Post($"masters/organizations/{sid}/deletion", new { confirmedImpact = preview.GetProperty("confirmedImpact") }, etag: source.GetProperty("etag").GetString())).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest)); Assert.That((await Post($"masters/organizations/{sid}/deletion", new { replacementId = tid, detachConfirmed = "yes", confirmedImpact = preview.GetProperty("confirmedImpact") }, etag: source.GetProperty("etag").GetString())).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest)); var key = Guid.NewGuid(); var body = new { replacementId = tid, detachConfirmed = !replace, confirmedImpact = preview.GetProperty("confirmedImpact") }; Assert.That((await Post($"masters/organizations/{sid}/deletion", body, key, source.GetProperty("etag").GetString())).StatusCode, Is.EqualTo(HttpStatusCode.NoContent)); Assert.That((await Post($"masters/organizations/{sid}/deletion", body, key, source.GetProperty("etag").GetString())).StatusCode, Is.EqualTo(HttpStatusCode.NoContent)); var productAfter = await client.GetFromJsonAsync<JsonElement>($"/api/v1/products/{pid}"); Assert.That(productAfter.GetProperty("totalQuantity").GetInt64(), Is.EqualTo(2)); Assert.That(productAfter.GetProperty("manufacturer").ValueKind, Is.EqualTo(replace ? JsonValueKind.Object : JsonValueKind.Null));
    }
}
