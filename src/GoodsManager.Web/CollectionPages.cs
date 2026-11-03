using Microsoft.Net.Http.Headers;

namespace GoodsManager.Web;

/// <summary>SPAの画面URLと静的ファイルを配信する。</summary>
public static class CollectionPages
{
    /// <summary>HTMLには共通の防御ヘッダーを付け、ハッシュ付きアセットの長期キャッシュを維持する。</summary>
    public static void MapCollectionPages(this WebApplication app)
    {
        app.UseStaticFiles(new StaticFileOptions
        {
            OnPrepareResponse = context =>
            {
                context.Context.Response.Headers.CacheControl = context.Context.Request.Path.StartsWithSegments("/assets") ? "public,max-age=31536000,immutable" : "no-store";
                if (context.Context.Response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) == true)
                {
                    ApplyHtmlHeaders(context.Context.Response);
                }
            }
        });

        /// <summary>画面URLへ直接アクセスした要求にSPAの入口を返す。</summary>
        async Task Index(HttpContext context)
        {
            var file = Path.Combine(app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot"), "index.html");
            ApplyHtmlHeaders(context.Response);
            await Results.File(file, "text/html; charset=utf-8").ExecuteAsync(context);
        }

        foreach (var path in new[] { "/", "/collection", "/collections", "/login", "/setup", "/account", "/settings", "/settings/images", "/register", "/masters", "/backups", "/trash", "/trash/products/{id:guid}", "/products/{id:guid}", "/products/{id:guid}/items/{itemId:guid}" })
        {
            app.MapMethods(path, ["GET", "HEAD"], Index);
        }
    }

    /// <summary>HTMLの配信経路によるCSPとキャッシュ方針の差をなくす。</summary>
    private static void ApplyHtmlHeaders(HttpResponse response)
    {
        response.Headers.CacheControl = "no-store";
        response.Headers[HeaderNames.ContentSecurityPolicy] = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' blob:; media-src 'self' blob:; connect-src 'self'; base-uri 'none'; form-action 'self'; frame-ancestors 'none'";
        response.Headers[HeaderNames.XContentTypeOptions] = "nosniff";
    }
}
