using GoodsManager.Web.Masters;
using GoodsManager.Web.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GoodsManager.Web.Products;

[Authorize]
[Route("api/v1/products")]
public sealed class ProductsController(AppDbContext db, ProductOperations products, InventoryOperations inventory, ProductWrites writes) : ControllerBase
{
    [HttpGet("{id:guid}")]
    public Task<IActionResult> Get(Guid id) => Read(id, (product, token) => products.DetailAsync(product, token));

    [HttpGet("{id:guid}/inventory")]
    public Task<IActionResult> GetInventory(Guid id) => Read(id, async (product, token) => await products.InventoryAsync(product, new Dictionary<string, Guid>(), token));

    [HttpGet("{id:guid}/items/{itemId:guid}")]
    public Task<IActionResult> GetItem(Guid id, Guid itemId) => Read(id, async (product, token) =>
    {
        var value = await products.InventoryAsync(product, new Dictionary<string, Guid>(), token);
        var item = value.Items.SingleOrDefault(x => x.Id == itemId) ?? throw new MasterProblem(404, "NOT_FOUND");
        return new { productId = id, value.ProductRevision, value.Etag, item };
    });

    [HttpPost("{id:guid}/inventory-commands")]
    [RequestSizeLimit(ProductWrites.BodyLimit)]
    public Task<IActionResult> Commands(Guid id) => writes.ExecuteAsync(HttpContext, $"POST:/api/v1/products/{id:D}/inventory-commands", false, async (body, now, token) =>
    {
        var product = await products.FindAsync(id, token);
        ProductWrites.CheckVersion(product, Request.Headers.IfMatch.ToString());
        var commands = ProductWrites.Parse(() => InventoryCommands.Parse(body));
        var refs = await inventory.ApplyAsync(product, commands, now, token, Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value));
        return (product, refs);
    });

    private async Task<IActionResult> Read(Guid id, Func<Product, CancellationToken, Task<object>> get)
    {
        var token = HttpContext.RequestAborted;
        await db.Database.OpenConnectionAsync(token);
        await using var native = ((SqliteConnection)db.Database.GetDbConnection()).BeginTransaction(deferred: true);
        await using var transaction = await db.Database.UseTransactionAsync(native, token);
        var product = await products.FindAsync(id, token);
        var value = await get(product, token);
        Response.Headers.ETag = ProductOperations.Etag(product);
        await native.CommitAsync(token);
        return Ok(value);
    }
}

/// <summary>重複確認・画像を接続するP08の正式登録APIとは分けた、開発確認用の作成入口。</summary>
[Authorize]
public sealed class DevelopmentProductsController(IHostEnvironment environment, ProductOperations products, ProductWrites writes) : ControllerBase
{
    [HttpPost("api/v1/dev/products")]
    [RequestSizeLimit(ProductWrites.BodyLimit)]
    public Task<IActionResult> Create()
    {
        if (!environment.IsDevelopment()) throw new MasterProblem(404, "NOT_FOUND");
        return writes.ExecuteAsync(HttpContext, "POST:/api/v1/dev/products", true, async (body, now, token) =>
        {
            var input = ProductWrites.Parse(() =>
            {
                var root = new InputObject(body, "product", "initialLot");
                var lot = new InputObject(root.Get("initialLot"), "quantity", "fields");
                return (Draft: InputObject.Product(root.Get("product")), Quantity: lot.Integer("quantity"), Fields: InputObject.Fields(lot.Get("fields"), true));
            });
            var product = await products.CreateAsync(input.Draft, input.Quantity, input.Fields, now, token);
            return (product, new Dictionary<string, Guid>());
        });
    }
}
