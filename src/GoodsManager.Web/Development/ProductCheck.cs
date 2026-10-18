namespace GoodsManager.Web.Development;

/// <summary>P05の開発確認画面。本番・Stagingでは配信しない。</summary>
public static class ProductCheck
{
    public static void MapProductCheck(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment()) return;
        Map(app, "/dev/product-check", "index.html", "text/html; charset=utf-8");
        Map(app, "/dev/product-check/product-check.js", "product-check.js", "text/javascript; charset=utf-8");
    }
    private static void Map(WebApplication app, string path, string name, string contentType)
    {
        app.MapGet(path, (HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.ContentSecurityPolicy = "default-src 'none'; script-src 'self'; style-src 'self'; connect-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            return Results.Stream(typeof(ProductCheck).Assembly.GetManifestResourceStream("GoodsManager.Web.Development.ProductCheck." + name)!, contentType);
        });
    }
}
