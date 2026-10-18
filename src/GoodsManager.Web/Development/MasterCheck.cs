namespace GoodsManager.Web.Development;

/// <summary>P04の手動確認用ファイルだけを開発環境で配信</summary>
public static class MasterCheck
{
    public static void MapMasterCheck(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment()) return;
        Map(app, "/dev/master-check", "index.html", "text/html; charset=utf-8");
        Map(app, "/dev/master-check/master-check.css", "master-check.css", "text/css; charset=utf-8");
        Map(app, "/dev/master-check/master-check.js", "master-check.js", "text/javascript; charset=utf-8");
        Map(app, "/dev/master-check/requests.mjs", "requests.mjs", "text/javascript; charset=utf-8");
    }

    private static void Map(WebApplication app, string path, string name, string contentType)
    {
        app.MapGet(path, (HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.ContentSecurityPolicy = "default-src 'none'; script-src 'self'; style-src 'self'; connect-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            var stream = typeof(MasterCheck).Assembly.GetManifestResourceStream("GoodsManager.Web.Development.MasterCheck." + name)!;
            return Results.Stream(stream, contentType);
        });
    }
}
