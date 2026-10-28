using GoodsManager.Web.Masters;
using GoodsManager.Web.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GoodsManager.Web.Books;

/// <summary>商品を変更せず、書誌と現時点の出版社候補を認証済み画面に返す。</summary>
[Authorize]
[Route("api/v1/book-metadata")]
public sealed class BooksController(NdlBooks books, AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(string? isbn)
    {
        var records = await books.FindAsync(isbn, HttpContext.RequestAborted);
        var names = records.SelectMany(x => x.Publishers).Select(MasterText.Normalize).Distinct().ToArray();
        var organizations = await db.Set<Organization>().AsNoTracking().Where(x => names.Contains(x.NormalizedName)).OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).ToArrayAsync(HttpContext.RequestAborted);
        return Ok(new
        {
            items = records.Select(x => new
            {
                x.Title,
                x.Volume,
                x.Edition,
                x.Authors,
                x.PublicationYear,
                x.SourceUrl,
                x.InProcess,
                publishers = x.Publishers.Select(name => new
                {
                    name,
                    matches = organizations.Where(o => o.NormalizedName == MasterText.Normalize(name)).Select(o => new { o.Id, o.Name, o.WebsiteUrl, o.Memo, o.CreatedAt })
                })
            })
        });
    }
}
