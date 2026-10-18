namespace GoodsManager.Web.Development;

/// <summary>開発環境だけに認証の手動検証画面を公開</summary>
public static class AuthenticationCheck
{
    /// <summary>公開ファイルを固定し、通常の静的ファイル配信とは分離</summary>
    public static void MapAuthenticationCheck(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            return;
        }

        MapAsset(app, "/dev/auth-check", "index.html", "text/html; charset=utf-8");
        MapAsset(app, "/dev/auth-check/auth-check.css", "auth-check.css", "text/css; charset=utf-8");
        MapAsset(app, "/dev/auth-check/auth-check.js", "auth-check.js", "text/javascript; charset=utf-8");
        MapAsset(app, "/dev/auth-check/passkey-serialization.js", "passkey-serialization.js", "text/javascript; charset=utf-8");
        app.MapGet("/dev/auth-check/config", (HttpContext context, IConfiguration configuration) =>
        {
            SetHeaders(context);
            var origin = new Uri(configuration["Authentication:Origin"] ?? "https://localhost:7443");
            return Results.Json(new { origin = origin.GetLeftPart(UriPartial.Authority), rpId = configuration["Authentication:RpId"] ?? origin.Host });
        });
    }

    /// <summary>パスや設定ファイルを入力として受け取らず埋め込みリソースだけを配信</summary>
    private static void MapAsset(WebApplication app, string path, string name, string contentType)
    {
        app.MapGet(path, (HttpContext context) =>
        {
            SetHeaders(context);
            var stream = typeof(AuthenticationCheck).Assembly.GetManifestResourceStream("GoodsManager.Web.Development.AuthCheck." + name)!;
            return Results.Stream(stream, contentType);
        });
    }

    /// <summary>検証用応答を保存させず、外部資源と埋め込みを拒否</summary>
    private static void SetHeaders(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.ContentSecurityPolicy = "default-src 'none'; script-src 'self'; style-src 'self'; connect-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";
        context.Response.Headers.XContentTypeOptions = "nosniff";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
    }
}
