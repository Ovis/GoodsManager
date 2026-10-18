using GoodsManager.Web.Masters;
using GoodsManager.Web.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GoodsManager.Web.Search;

[Authorize]
[Route("api/v1")]
public sealed class SearchController(AppDbContext db, ProductSearch search) : ControllerBase
{
    [HttpGet("products")]
    public Task<IActionResult> Products() => Read(async token => await search.PageAsync(SearchQuery.Parse(Request.Query), token));
    [HttpGet("collection-summary")]
    public Task<IActionResult> Summary() => Read(async token =>
    {
        var query = SearchQuery.Parse(Request.Query);
        return await search.SummaryAsync(query.Inventory, token);
    });
    [HttpGet("suggestions/ownership")]
    public Task<IActionResult> Suggestions() => Read(async token =>
    {
        var field = Request.Query["field"].ToString(); var text = Request.Query["q"].ToString();
        var limitText = Request.Query["limit"].ToString();
        var limit = limitText.Length == 0 ? 20 : int.TryParse(limitText, out var number) ? number : 0;
        if (field is not ("storageLocation" or "purchasePlace") || limit is < 1 or > 20 || !StorageValues.IsValidText(text, 0, 200) || Request.Query["q"].Count > 1 || Request.Query["field"].Count != 1 || Request.Query["limit"].Count > 1) throw MasterInput.Invalid();
        return new { values = await search.SuggestionsAsync(field, text, limit, token) };
    });
    private async Task<IActionResult> Read(Func<CancellationToken, Task<object>> get)
    {
        var token = HttpContext.RequestAborted;
        await db.Database.OpenConnectionAsync(token);
        await using var native = ((SqliteConnection)db.Database.GetDbConnection()).BeginTransaction(deferred: true);
        await using var transaction = await db.Database.UseTransactionAsync(native, token);
        var value = await get(token); await native.CommitAsync(token); return Ok(value);
    }
}
