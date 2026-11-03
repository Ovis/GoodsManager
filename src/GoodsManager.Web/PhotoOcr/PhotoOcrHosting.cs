using System.Security.Claims;
using GoodsManager.Web.Collections;
using GoodsManager.Web.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;

namespace GoodsManager.Web.PhotoOcr;

/// <summary>ブラウザOCRの固定資産だけを扱う。写真・認識結果を受信しない。</summary>
public sealed class PhotoOcrAssets(string directory)
{
    public string DirectoryPath { get; } = Path.GetFullPath(directory);
    public bool Available => new[] { "workers/ocr.worker.js", "models/PP-OCRv5_mobile_det_onnx_infer.tar", "models/PP-OCRv5_mobile_rec_onnx_infer.tar", "runtime/ort/ort-wasm-simd-threaded.wasm" }.All(x => File.Exists(Path.Combine(DirectoryPath, x)));

    public string? Resolve(string asset)
    {
        if (asset.Contains('\\') || asset.Split('/').Any(x => x is ".." or "." or ""))
        {
            return null;
        }

        if (!(asset.StartsWith("workers/", StringComparison.Ordinal) || asset.StartsWith("assets/", StringComparison.Ordinal) || asset.StartsWith("models/", StringComparison.Ordinal) || asset.StartsWith("runtime/ort/", StringComparison.Ordinal) || asset.StartsWith("notices/", StringComparison.Ordinal)))
        {
            return null;
        }

        try
        {
            var path = Path.GetFullPath(Path.Combine(DirectoryPath, asset));
            return path.StartsWith(DirectoryPath + Path.DirectorySeparatorChar, StringComparison.Ordinal) && File.Exists(path) ? path : null;
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException) { return null; }
    }
}

[Authorize]
[ApiController]
[Route("api/v1/registration/photo-ocr")]
public sealed class RegistrationPhotoOcrController(AppDbContext db, PhotoOcrAssets assets) : ControllerBase
{
    [HttpGet]
    public IActionResult Access() => Ok(new
    {
        available = assets.Available,
        assetBase = $"/registration-photo-ocr/{User.FindFirstValue(ClaimTypes.NameIdentifier)}/{db.CollectionId:D}/"
    });
}

public static class PhotoOcrHosting
{
    public static void MapRegistrationPhotoOcr(this WebApplication app)
    {
        // Worker/SDK内部の取得は独自ヘッダーを付けられないため、要求先をURLに固定して毎回照合する。
        app.MapGet("/registration-photo-ocr/{accountId:guid}/{collectionId:guid}/{**asset}", async (HttpContext context, AppDbContext db, PhotoOcrAssets assets, Guid accountId, Guid collectionId, string? asset) =>
        {
            context.Response.Headers.CacheControl = "private, no-store";
            if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var current) || current != accountId)
            {
                return Results.StatusCode(409);
            }

            if (!await db.Set<CollectionMember>().AnyAsync(x => x.UserId == current && x.CollectionId == collectionId && (x.Role == "manager" || x.Role == "editor"), context.RequestAborted))
            {
                return Results.NotFound();
            }

            var path = asset is null ? null : assets.Resolve(asset);
            if (path is null)
            {
                return Results.NotFound();
            }

            context.Response.Headers.XContentTypeOptions = "nosniff";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";

            var eval = asset == "workers/ocr.worker.js" ? "'unsafe-eval'" : "'wasm-unsafe-eval'";
            context.Response.Headers.ContentSecurityPolicy = $"default-src 'none'; script-src 'self' {eval}; worker-src 'self'; connect-src 'self'; img-src 'self' blob:; object-src 'none'; base-uri 'none'; frame-ancestors 'none'";
            new FileExtensionContentTypeProvider().TryGetContentType(path, out var type);
            if (type?.StartsWith("text/", StringComparison.Ordinal) == true || type is "application/javascript" or "application/json")
            {
                type += "; charset=utf-8";
            }

            return Results.File(path, type ?? "application/octet-stream");
        }).RequireAuthorization();
    }
}
