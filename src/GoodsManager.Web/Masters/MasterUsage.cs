using GoodsManager.Web.Products;
using Microsoft.EntityFrameworkCore;

namespace GoodsManager.Web.Masters;

public sealed partial class MastersController
{
    private async Task<object> UsageAsync(string kind, Guid id)
    {
        var products = db.Set<Product>().AsNoTracking();
        var query = kind switch
        {
            "works" => products.Where(p => db.Set<ProductWork>().Any(r => r.ProductId == p.Id && r.WorkId == id)),
            "series" => products.Where(p => db.Set<ProductWork>().Any(r => r.ProductId == p.Id && db.Set<WorkSeries>().Any(s => s.WorkId == r.WorkId && s.SeriesId == id))),
            "characters" => products.Where(p => db.Set<ProductCharacter>().Any(r => r.ProductId == p.Id && r.CharacterId == id)),
            "tags" => products.Where(p => db.Set<ProductTag>().Any(r => r.ProductId == p.Id && r.TagId == id)),
            "organizations" => products.Where(p => p.ManufacturerId == id || p.SellerId == id),
            "categories" => products.Where(p => p.CategoryId == id),
            _ => products.Where(p => false)
        };
        return new
        {
            activeProductCount = await query.LongCountAsync(p => p.DeletedAt == null, HttpContext.RequestAborted),
            trashedProductCount = await query.LongCountAsync(p => p.DeletedAt != null, HttpContext.RequestAborted)
        };
    }
}
