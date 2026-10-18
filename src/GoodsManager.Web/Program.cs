using GoodsManager.Web;
using GoodsManager.Web.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using GoodsManager.Web.Auth;
using Microsoft.AspNetCore.Antiforgery;
using System.Text.Json.Serialization;
using GoodsManager.Web.Development;

var recovery = args.FirstOrDefault() == "recover-account";
var builder = WebApplication.CreateBuilder(recovery ? [] : args);
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
});
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddSingleton(services => new DatabaseRuntime(services.GetRequiredService<IConfiguration>()["Storage:Directory"] ?? Path.Combine(builder.Environment.ContentRootPath, "data")));
builder.Services.AddDbContext<AppDbContext>((services, options) => options.UseSqlite(services.GetRequiredService<DatabaseRuntime>().ConnectionString));
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);
builder.AddAppAuthentication();
builder.Services.AddScoped<OperationVerification>();
builder.Services.AddScoped<AccountRecovery>();
builder.Services.AddControllers().AddJsonOptions(options => options.JsonSerializerOptions.NumberHandling = JsonNumberHandling.Strict);

var app = builder.Build();
var runtime = app.Services.GetRequiredService<DatabaseRuntime>();
runtime.AcquireLease();
try
{
    await using var scope = app.Services.CreateAsyncScope();
    await DatabaseMaintenance.MigrateAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), runtime.DirectoryPath);
}
catch
{
    runtime.Dispose();
    throw;
}
app.Lifetime.ApplicationStopped.Register(runtime.Dispose);
if (recovery)
{
    try
    {
        Environment.ExitCode = await AccountRecovery.RunCliAsync(app.Services, args);
    }
    finally
    {
        runtime.Dispose();
        await app.DisposeAsync();
    }
    return;
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.Use(async (context, next) =>
{
    // 認証のチャレンジが短絡した場合もAPI応答をキャッシュさせない
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.Headers.CacheControl = "private, no-store";
    }

    await next(context);
});
app.UseAuthentication();
app.UseAuthorization();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method) && !HttpMethods.IsOptions(context.Request.Method))
        {
            try
            {
                await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context);
            }
            catch (AntiforgeryValidationException)
            {
                await Results.Problem(statusCode: 403, title: "CSRFトークンを確認してください", extensions: new Dictionary<string, object?> { ["code"] = "CSRF_FAILED" }).ExecuteAsync(context);
                return;
            }
        }
    }

    await next(context);
});
app.MapControllers();
app.MapAuthenticationCheck();
app.MapMasterCheck();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
app.Run();

/// <summary>HTTP統合試験からアプリの起動点を参照</summary>
public partial class Program;
