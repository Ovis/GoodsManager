using GoodsManager.Web.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GoodsManager.Web.Images;

[Authorize]
[Route("api/v1/system/alerts")]
public sealed class SystemAlertsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken token) => Ok(new { entries = await db.Set<SystemAlert>().AsNoTracking().Where(x => x.ResolvedAt == null).OrderByDescending(x => x.LastSeenAt).Select(x => new { id = x.Id, x.Code, x.Severity, x.SafeDetailsJson, x.FirstSeenAt, x.LastSeenAt }).ToArrayAsync(token) });
}
