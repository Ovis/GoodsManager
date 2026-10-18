using System.Text.Json;
using GoodsManager.Web.Images;
using GoodsManager.Web.Masters;
using GoodsManager.Web.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GoodsManager.Web.Products;

/// <summary>正式登録と既存への所持追加。保存と成功応答の記録は共通の書込処理へ任せる</summary>
[Authorize]
[Route("api/v1")]
public sealed class RegistrationController(AppDbContext db, ProductOperations products, InventoryOperations inventory, ProductWrites writes, ImageLinks images, DuplicateChecks duplicates) : ControllerBase
{
    /// <summary>候補と入力識別値を同じ読取スナップショットから返す</summary>
    [HttpPost("duplicate-checks")]
    [RequestSizeLimit(ProductWrites.BodyLimit)]
    public async Task<IActionResult> Check()
    {
        var token = HttpContext.RequestAborted;
        using var document = await RegistrationJson.ReadAsync(Request, token);
        var input = ProductWrites.Parse(() => { var x = new InputObject(document.RootElement, "input", "excludeProductId"); return (Value: DuplicateInput.Parse(x.Get("input")), Exclude: x.OptionalId("excludeProductId")); });
        await db.Database.OpenConnectionAsync(token);
        await using var native = ((SqliteConnection)db.Database.GetDbConnection()).BeginTransaction(deferred: true);
        await using var tx = await db.Database.UseTransactionAsync(native, token);
        var result = await duplicates.CheckAsync(input.Value, input.Exclude, token); await native.CommitAsync(token); return Ok(result);
    }
    /// <summary>重複確認・商品・所持・画像・成功応答を一括確定</summary>
    [HttpPost("products")]
    [RequestSizeLimit(ProductWrites.BodyLimit)]
    public Task<IActionResult> Create() => writes.ExecuteAsync(HttpContext, "POST:/api/v1/products", true, async (body, now, token) =>
    {
        var input = ProductWrites.Parse(() =>
        {
            var x = new InputObject(body, "product", "initialLot", "images", "duplicateConfirmation");
            var lot = new InputObject(x.Get("initialLot"), "quantity", "fields");
            return (Draft: RegistrationLimits.Product(InputObject.Product(x.Get("product"))), Quantity: lot.Integer("quantity"), Fields: InputObject.Fields(lot.Get("fields"), true), Uploads: RegistrationJson.Uploads(x), Confirmation: DuplicateConfirmation.Parse(x));
        });
        // 参照・属性を検証し、未確定の追加内容は外側のトランザクションに留める。
        var product = await products.CreateAsync(input.Draft, input.Quantity, input.Fields, now, token);
        await duplicates.RequireConfirmationAsync(DuplicateInput.From(input.Draft), input.Confirmation, "createSeparate", null, token);
        await images.AppendProductAsync(product.Id, UserId(), input.Uploads, token);
        return (product, new Dictionary<string, Guid>());
    }, async (p, token) => new { product = await products.DetailAsync(p, token), outcome = "created", deduplicatedCount = images.DeduplicatedCount });

    /// <summary>商品属性を変更せず、確認済みの既存商品へ所持と画像を追加</summary>
    [HttpPost("products/{id:guid}/holdings")]
    [RequestSizeLimit(ProductWrites.BodyLimit)]
    public Task<IActionResult> Add(Guid id) => writes.ExecuteAsync(HttpContext, $"POST:/api/v1/products/{id:D}/holdings", false, async (body, now, token) =>
    {
        var input = ProductWrites.Parse(() =>
        {
            var x = new InputObject(body, "input", "lot", "images", "duplicateConfirmation");
            var lot = new InputObject(x.Get("lot"), "quantity", "fields");
            return (Value: DuplicateInput.Parse(x.Get("input")), Quantity: lot.Integer("quantity"), Fields: InputObject.Fields(lot.Get("fields"), true), Uploads: RegistrationJson.Uploads(x), Confirmation: DuplicateConfirmation.Parse(x));
        });
        var product = await products.FindAsync(id, token); ProductWrites.CheckVersion(product, Request.Headers.IfMatch.ToString());
        await duplicates.RequireConfirmationAsync(input.Value, input.Confirmation, "addToExisting", id, token);
        var refs = await inventory.ApplyAsync(product, [new CreateLot("added", input.Quantity, input.Fields)], now, token, UserId());
        await images.AppendProductAsync(id, UserId(), input.Uploads, token);
        return (product, refs);
    }, async (p, token) => new { product = await products.DetailAsync(p, token), outcome = "addedToExisting", deduplicatedCount = images.DeduplicatedCount });

    /// <summary>重複確認と全商品属性の編集を、親の更新条件と同一トランザクションで確定</summary>
    [HttpPut("products/{id:guid}")]
    [RequestSizeLimit(ProductWrites.BodyLimit)]
    public Task<IActionResult> Update(Guid id) => writes.ExecuteAsync(HttpContext, $"PUT:/api/v1/products/{id:D}", false, async (body, now, token) =>
    {
        var product = await products.FindAsync(id, token); ProductWrites.CheckVersion(product, Request.Headers.IfMatch.ToString());
        var input = ProductWrites.Parse(() => { var x = new InputObject(body, "product", "duplicateConfirmation"); return (Draft: RegistrationLimits.Product(InputObject.Product(x.Get("product"))), Confirmation: DuplicateConfirmation.Parse(x)); });
        var before = await products.DraftAsync(product, token);
        // 属性・参照を先に検証しても、確定と更新番号の変更は外側のトランザクションに留まる。
        await products.UpdateAsync(product, input.Draft, now, token);
        await duplicates.RequireEditConfirmationAsync(before, input.Draft, input.Confirmation, id, token);
        return (product, (IReadOnlyDictionary<string, Guid>)new Dictionary<string, Guid>());
    }, (p, token) => products.DetailAsync(p, token));
    /// <summary>認証済みユーザーの画像所有者IDを取得</summary>
    private Guid UserId() => Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
}

internal static class RegistrationJson
{
    /// <summary>サーバーの種類にかかわらずJSON本文のサイズを制限</summary>
    public static async Task<JsonDocument> ReadAsync(HttpRequest request, CancellationToken token)
    {
        if (!request.HasJsonContentType()) throw new MasterProblem(415, "UNSUPPORTED_MEDIA_TYPE");
        var bytes = new byte[ProductWrites.BodyLimit + 1]; var length = 0;
        while (length < bytes.Length) { var count = await request.Body.ReadAsync(bytes.AsMemory(length), token); if (count == 0) break; length += count; }
        if (length > ProductWrites.BodyLimit) throw new MasterProblem(413, "REQUEST_TOO_LARGE");
        return ProductWrites.Parse(() => JsonDocument.Parse(bytes.AsMemory(0, length)));
    }
    /// <summary>画像の要求件数とアップロードIDの重複を検証</summary>
    public static Guid[] Uploads(InputObject root)
    {
        var result = RegistrationLimits.Array(root.Array("images"), "images").Select(v => new InputObject(v, "temporaryUploadId").Id("temporaryUploadId")).ToArray();
        if (result.Distinct().Count() != result.Length) throw InputObject.Invalid("images");
        return result;
    }
}

internal static class RegistrationLimits
{
    /// <summary>保存総数とは分けて一要求の配列件数を制限</summary>
    public static T[] Array<T>(T[] values, string field)
    {
        if (values.Length > 100) throw InputObject.Invalid(field);
        return values;
    }
    /// <summary>正式登録で受け付ける関連・コード・リンクの件数を検証</summary>
    public static ProductDraft Product(ProductDraft draft)
    {
        Array(draft.WorkIds, "workIds"); Array(draft.CharacterIds, "characterIds"); Array(draft.TagIds, "tagIds"); Array(draft.Identifiers, "identifiers"); Array(draft.Links, "links");
        return draft;
    }
}
