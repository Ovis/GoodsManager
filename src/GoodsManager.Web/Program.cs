using GoodsManager.Web;
using GoodsManager.Web.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
});
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
var runtime = new DatabaseRuntime(builder.Configuration["Storage:Directory"] ?? Path.Combine(builder.Environment.ContentRootPath, "data"));
builder.Services.AddSingleton(runtime);
builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite(runtime.ConnectionString));
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

var app = builder.Build();
runtime.AcquireLease();
app.Lifetime.ApplicationStopped.Register(runtime.Dispose);
await using (var scope = app.Services.CreateAsyncScope())
{
    await DatabaseMaintenance.MigrateAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), runtime.DirectoryPath);
}
app.UseExceptionHandler();
app.UseStatusCodePages();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
app.Run();

/// <summary>HTTP統合試験からアプリの起動点を参照</summary>
public partial class Program;
