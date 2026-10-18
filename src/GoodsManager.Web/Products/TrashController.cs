using GoodsManager.Web.Masters;
using GoodsManager.Web.Search;
using GoodsManager.Web.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GoodsManager.Web.Products;

/// <summary>無期限ゴミ箱の閲覧と、親の更新条件を使う移動・復元・完全削除</summary>
[Authorize]
[Route("api/v1")]
public sealed class TrashController(AppDbContext db, ProductOperations products, ProductWrites writes, ProductSearch search, DuplicateChecks duplicates) : ControllerBase
{
    /// <summary>通常検索と同じ文字・ID条件を、削除済み商品だけに適用</summary>
    [HttpGet("trash/products")]
    public Task<IActionResult> List() => Read(async token =>
    {
        if (Request.Query["sort"].Count > 1 || Request.Query["sort"].Count == 1 && Request.Query["sort"] != "deletedDesc") throw MasterInput.Invalid("sort");
        var values = Request.Query.ToDictionary(x => x.Key, x => x.Value);
        values.Remove("sort"); values["inventory"] = "all";
        return await search.PageAsync(SearchQuery.Parse(new QueryCollection(values)), token, trash: true);
    });

    /// <summary>削除済み商品を編集できない閲覧用の詳細として取得</summary>
    [HttpGet("trash/products/{id:guid}")]
    public Task<IActionResult> Get(Guid id) => Read(async token =>
    {
        var product = await FindTrashed(id, token); Response.Headers.ETag = ProductOperations.Etag(product);
        return await products.DetailAsync(product, token);
    });

    /// <summary>所持・画像・分類を残し、通常の商品編集から外す</summary>
    [HttpDelete("products/{id:guid}")]
    [RequestSizeLimit(ProductWrites.BodyLimit)]
    public Task<IActionResult> Trash(Guid id) => writes.ExecuteAsync(HttpContext, $"DELETE:/api/v1/products/{id:D}", false, async (body, now, token) =>
    {
        _ = new InputObject(body); var product = await products.FindAsync(id, token);
        ProductWrites.CheckVersion(product, Request.Headers.IfMatch.ToString());
        product.DeletedAt = now; await products.TouchAsync(product, now, token);
        return (product, new Dictionary<string, Guid>());
    }, noContent: true, changeKind: "trashed");

    /// <summary>自分を重複候補から除き、確認済みの最新候補だけで別商品として復元</summary>
    [HttpPost("trash/products/{id:guid}/restore")]
    [RequestSizeLimit(ProductWrites.BodyLimit)]
    public Task<IActionResult> Restore(Guid id) => writes.ExecuteAsync(HttpContext, $"POST:/api/v1/trash/products/{id:D}/restore", false, async (body, now, token) =>
    {
        var input = ProductWrites.Parse(() => new InputObject(body, "duplicateConfirmation"));
        var product = await FindTrashed(id, token); ProductWrites.CheckVersion(product, Request.Headers.IfMatch.ToString());
        var draft = await products.DraftAsync(product, token);
        await duplicates.RequireConfirmationAsync(DuplicateInput.From(draft), DuplicateConfirmation.Parse(input), "restore", id, token, excludeProductId: id);
        product.DeletedAt = null; await products.TouchAsync(product, now, token);
        return (product, new Dictionary<string, Guid>());
    }, (p, token) => products.DetailAsync(p, token), changeKind: "restored");

    /// <summary>確認した対象IDも再送条件に含め、参照だけを一括削除。共有ファイルは既存GCへ任せる</summary>
    [HttpDelete("trash/products/{id:guid}")]
    [RequestSizeLimit(ProductWrites.BodyLimit)]
    public Task<IActionResult> Purge(Guid id) => writes.ExecuteAsync(HttpContext, $"DELETE:/api/v1/trash/products/{id:D}", false, async (body, now, token) =>
    {
        _ = new InputObject(body);
        if (Request.Headers["X-Confirm-Delete"].ToString() != id.ToString("D")) throw MasterInput.Invalid("confirmDelete");
        var product = await FindTrashed(id, token); ProductWrites.CheckVersion(product, Request.Headers.IfMatch.ToString());
        await products.TouchAsync(product, now, token); db.Remove(product);
        return (product, new Dictionary<string, Guid>());
    }, noContent: true, changeKind: "purged");

    /// <summary>通常商品をゴミ箱用の更新入口へ持ち込ませない</summary>
    private async Task<Product> FindTrashed(Guid id, CancellationToken token) => await db.Set<Product>().SingleOrDefaultAsync(x => x.Id == id && x.DeletedAt != null, token) ?? throw new MasterProblem(404, "NOT_FOUND");

    /// <summary>詳細・件数・分類・画像を同じ読取スナップショットに固定</summary>
    private async Task<IActionResult> Read(Func<CancellationToken, Task<object>> read)
    {
        var token = HttpContext.RequestAborted; await db.Database.OpenConnectionAsync(token);
        await using var native = ((SqliteConnection)db.Database.GetDbConnection()).BeginTransaction(deferred: true);
        await using var transaction = await db.Database.UseTransactionAsync(native, token);
        var result = await read(token); await native.CommitAsync(token); return Ok(result);
    }
}
