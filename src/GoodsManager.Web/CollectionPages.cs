using Microsoft.Net.Http.Headers;
namespace GoodsManager.Web;

public static class CollectionPages
{
    public static void MapCollectionPages(this WebApplication app)
    {
        app.UseStaticFiles(new StaticFileOptions
        {
            OnPrepareResponse = context =>
            {
                context.Context.Response.Headers.CacheControl = context.Context.Request.Path.StartsWithSegments("/assets") ? "public,max-age=31536000,immutable" : "no-store";
            }
        });
        async Task Index(HttpContext context)
        {
            var file = Path.Combine(app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot"), "index.html");
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers[HeaderNames.ContentSecurityPolicy] = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' blob:; connect-src 'self'; base-uri 'none'; form-action 'self'; frame-ancestors 'none'";
            context.Response.Headers[HeaderNames.XContentTypeOptions] = "nosniff";
            await Results.File(file, "text/html; charset=utf-8").ExecuteAsync(context);
        }
        foreach (var path in new[] { "/", "/collection", "/login", "/register", "/products/{id:guid}" }) app.MapGet(path, Index);
    }
}
