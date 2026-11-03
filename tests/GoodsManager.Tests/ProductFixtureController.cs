using GoodsManager.Web.Products;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GoodsManager.Tests;

/// <summary>所持・画像等のAPI試験に必要な商品を準備する、試験ホスト専用の作成入口。</summary>
[Authorize]
public sealed class ProductFixtureController(ProductOperations products, ProductWrites writes) : ControllerBase
{
    /// <summary>通常登録の重複確認から独立して、商品・初期所持・再送記録を同時に作成する。</summary>
    [HttpPost("api/v1/dev/products")]
    [RequestSizeLimit(ProductWrites.BodyLimit)]
    public Task<IActionResult> Create()
    {
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
