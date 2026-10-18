using GoodsManager.Web.Auth;
using GoodsManager.Web.Masters;
using GoodsManager.Web.Products;
using GoodsManager.Web.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GoodsManager.Web.Images;

[Authorize]
public sealed class ImageMediaController(AppDbContext db, IImageFiles files, AuthWriteGate gate, ImageAlerts alerts, SignInManager<AppUser> signIn, TimeProvider clock) : ControllerBase
{
    [HttpGet("media/assets/{id:guid}/{variant}")]
    public Task<IActionResult> Media(Guid id, string variant) => StreamAsync(async token =>
    {
        var referenced = await db.Set<ProductImage>().AnyAsync(x => x.ImageAssetId == id, token) || await db.Set<InventoryItemImage>().AnyAsync(x => x.ImageAssetId == id, token);
        if (!referenced) throw new MasterProblem(404, "NOT_FOUND");
        var asset = await db.Set<ImageAsset>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, token) ?? throw new MasterProblem(404, "NOT_FOUND");
        return variant switch { "thumbnail" => asset.ThumbnailFileId, "display" => asset.DisplayFileId, "original" when asset.KeepOriginal => asset.OriginalFileId!.Value, _ => throw new MasterProblem(404, "NOT_FOUND") };
    });
    [HttpGet("api/v1/temporary-uploads/{id:guid}/preview")]
    public Task<IActionResult> Preview(Guid id) => StreamAsync(async token =>
    {
        var userId = Guid.Parse(signIn.UserManager.GetUserId(User)!);
        var upload = await db.Set<TemporaryUpload>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.UserId == userId && x.State == "ready" && x.ExpiresAt > clock.GetUtcNow(), token) ?? throw new MasterProblem(404, "NOT_FOUND");
        return (await db.Set<ImageAsset>().AsNoTracking().SingleAsync(x => x.Id == upload.ImageAssetId, token)).DisplayFileId;
    });
    private async Task<IActionResult> StreamAsync(Func<CancellationToken, Task<Guid>> select)
    {
        var token = HttpContext.RequestAborted; Response.Headers.CacheControl = "private, no-store"; Response.Headers["X-Content-Type-Options"] = "nosniff"; Response.Headers["Content-Security-Policy"] = "default-src 'none'; sandbox";
        await gate.Semaphore.WaitAsync(token);
        try
        {
            if (await AuthenticationState.GetCurrentUserAsync(db, signIn, User) is null) throw new MasterProblem(401, "SESSION_EXPIRED");
            var id = await select(token); var file = await db.Set<ImageFile>().AsNoTracking().SingleAsync(x => x.Id == id, token);
            FileStream stream;
            try { await files.VerifyAsync(file, token); stream = new FileStream(files.Resolve(file.RelativePath), FileMode.Open, FileAccess.Read, FileShare.Read); }
            catch (IOException)
            {
                await alerts.SetAsync("image-file:" + id.ToString("D"), "IMAGE_FILE_UNAVAILABLE", id, token);
                throw new MasterProblem(503, "IMAGE_FILE_UNAVAILABLE");
            }
            await alerts.ResolveAsync("image-file:" + id.ToString("D"), token);
            return File(stream, file.MediaType, enableRangeProcessing: false);
        }
        finally { gate.Semaphore.Release(); }
    }
}
