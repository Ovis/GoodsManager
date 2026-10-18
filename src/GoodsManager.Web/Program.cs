using GoodsManager.Web.Operations;
using GoodsManager.Web;
using GoodsManager.Web.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using GoodsManager.Web.Auth;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.HttpOverrides;
using System.Text.Json.Serialization;
using GoodsManager.Web.Development;
using GoodsManager.Web.Products;
using GoodsManager.Web.Search;
using GoodsManager.Web.Images;

if (args.FirstOrDefault() == "healthcheck") { Environment.ExitCode = await HealthCommand.RunAsync(); return; }
if (args.FirstOrDefault() == "image-worker") { Environment.ExitCode = ImageConverter.RunCli(args); return; }
if (args.FirstOrDefault() == "backup")
{
    var cli = WebApplication.CreateBuilder([]);
    Environment.ExitCode = await GoodsManager.Web.Backups.BackupCli.RunAsync(cli.Configuration, cli.Environment.ContentRootPath, args[1..]);
    return;
}
var recovery = args.FirstOrDefault() == "recover-account";
var builder = WebApplication.CreateBuilder(recovery ? [] : args);
// 承認した7種各100件のID条件と200コードポイントの検索値をURLで扱う。
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestLineSize = 64 * 1024);
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
});
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddSingleton(services => new DatabaseRuntime(services.GetRequiredService<IConfiguration>()["Storage:Directory"] ?? Path.Combine(builder.Environment.ContentRootPath, "data")));
builder.Services.AddDbContext<AppDbContext>((services, options) => options.UseSqlite(services.GetRequiredService<DatabaseRuntime>().ConnectionString));
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);
builder.Services.AddSingleton<GoodsManager.Web.Backups.BackupGate>();
builder.Services.AddSingleton<GoodsManager.Web.Backups.BackupOptions>();
builder.Services.AddSingleton<GoodsManager.Web.Backups.BackupSignal>();
builder.Services.AddScoped<GoodsManager.Web.Backups.BackupService>();
builder.Services.AddHostedService<GoodsManager.Web.Backups.BackupWorker>();
builder.AddAppAuthentication();
builder.Logging.ClearProviders();
builder.Services.AddSingleton<OperationalLogging>();
builder.Services.AddHostedService<OperationalLogWorker>();
builder.Services.AddSingleton<ILoggerProvider>(services => services.GetRequiredService<OperationalLogging>());
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardLimit = 1;
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    // 接続元の制限は配置側で担保し、直近のプロキシの転送情報を採用する。
    options.KnownProxies.Clear(); options.KnownIPNetworks.Clear();
    options.AllowedHosts.Add(new Uri(builder.Configuration["Authentication:Origin"] ?? "https://localhost:7443").Host);
});
builder.Services.AddScoped<OperationVerification>();
builder.Services.AddScoped<AccountRecovery>();
builder.Services.AddScoped<ProductOperations>();
builder.Services.AddScoped<InventoryOperations>();
builder.Services.AddScoped<ProductWrites>();
builder.Services.AddScoped<DuplicateChecks>();
builder.Services.AddScoped<SearchProjection>();
builder.Services.AddScoped<ProductSearch>();
builder.Services.AddSingleton<ImageLimits>();
builder.Services.AddSingleton<ImageActivity>();
builder.Services.AddSingleton<IImageFiles, ImageFiles>();
builder.Services.AddSingleton<IImageConverter, IsolatedImageConverter>();
builder.Services.AddScoped<ImageUploads>();
builder.Services.AddScoped<ImageLinks>();
builder.Services.AddScoped<ImageGc>();
builder.Services.AddScoped<ImageAlerts>();
builder.Services.AddHostedService<ImageWorker>();
builder.Services.AddControllers().AddJsonOptions(options => options.JsonSerializerOptions.NumberHandling = JsonNumberHandling.Strict);

var app = builder.Build();
var runtime = app.Services.GetRequiredService<DatabaseRuntime>();
runtime.AcquireLease();
try
{
    foreach (var directory in new[] { "images", "keys" })
    {
        var path = Path.Combine(runtime.DirectoryPath, directory);
        GoodsManager.Web.Backups.BackupPaths.RejectLinks(path); Directory.CreateDirectory(path);
    }
    await using var scope = app.Services.CreateAsyncScope();
    await DatabaseMaintenance.MigrateAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), runtime.DirectoryPath, app.Services.GetRequiredService<GoodsManager.Web.Backups.BackupOptions>(), app.Services.GetRequiredService<GoodsManager.Web.Backups.BackupGate>().KeyLock);
    await scope.ServiceProvider.GetRequiredService<SearchProjection>().RebuildAllAsync();
    await ImageWorker.InitializeAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), scope.ServiceProvider.GetRequiredService<TimeProvider>());
    if (!recovery)
    {
        await scope.ServiceProvider.GetRequiredService<ImageGc>().RunAsync(CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<GoodsManager.Web.Backups.BackupService>().InitializeAsync(CancellationToken.None);
    }
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

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.Use(async (context, next) =>
{
    // 認証のチャレンジが短絡した場合もAPI応答をキャッシュさせない
    if (context.Request.Path.StartsWithSegments("/api") || context.Request.Path.StartsWithSegments("/media"))
    {
        context.Response.Headers.CacheControl = "private, no-store";
    }

    await next(context);
});
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/health")) { await next(context); return; }
    if (!app.Environment.IsDevelopment())
    {
        var origin = new Uri(app.Configuration["Authentication:Origin"]!);
        if (!context.Request.IsHttps || (!string.Equals(context.Request.Host.Host, origin.Host, StringComparison.OrdinalIgnoreCase) || (context.Request.Host.Port ?? 443) != origin.Port))
        {
            await Results.Problem(statusCode: 400, title: "公開HTTPSオリジンを確認してください", extensions: new Dictionary<string, object?> { ["code"] = "PUBLIC_ORIGIN_REQUIRED" }).ExecuteAsync(context);
            return;
        }
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
app.MapCollectionPages();
app.MapControllers();
app.MapAuthenticationCheck();
app.MapMasterCheck();
app.MapProductCheck();
app.MapImageCheck();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("GoodsManager.Web.Operations").LogInformation(new EventId(1000), "運用開始 Code={Code}", "APPLICATION_READY");
app.Run();

/// <summary>HTTP統合試験からアプリの起動点を参照</summary>
public partial class Program;
