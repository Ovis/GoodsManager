namespace GoodsManager.Web.Development;

public static class ImageCheck
{
    public static void MapImageCheck(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment()) return;
        Map("/dev/image-check", "index.html", "text/html; charset=utf-8");
        Map("/dev/image-check/image-check.css", "image-check.css", "text/css; charset=utf-8");
        Map("/dev/image-check/image-check.js", "image-check.js", "text/javascript; charset=utf-8");
        void Map(string path, string name, string type) => app.MapGet(path, (HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.ContentSecurityPolicy = "default-src 'none'; script-src 'self'; style-src 'self'; connect-src 'self'; img-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            return Results.Stream(typeof(ImageCheck).Assembly.GetManifestResourceStream("GoodsManager.Web.Development.ImageCheck." + name)!, type);
        });
    }
}
