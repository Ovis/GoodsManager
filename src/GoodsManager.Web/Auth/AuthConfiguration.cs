using GoodsManager.Web.Storage;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace GoodsManager.Web.Auth;

/// <summary>標準Identity、クッキー認証とCSRFの構成</summary>
public static class AuthConfiguration
{
    /// <summary>秘密の保存、絶対期限、毎要求の失効確認を構成</summary>
    public static void AddAppAuthentication(this WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<AuthWriteGate>();
        builder.Services.AddSingleton<PasskeyCeremonies>();
        builder.Services.AddDataProtection().SetApplicationName("GoodsManager");
        builder.Services.AddOptions<Microsoft.AspNetCore.DataProtection.KeyManagement.KeyManagementOptions>()
            .Configure<DatabaseRuntime, ILoggerFactory>((options, runtime, loggerFactory) =>
                options.XmlRepository = new Microsoft.AspNetCore.DataProtection.Repositories.FileSystemXmlRepository(new DirectoryInfo(Path.Combine(runtime.DirectoryPath, "keys")), loggerFactory));
        builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
        builder.Services.AddIdentityCore<AppUser>(options =>
        {
            options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
            options.User.AllowedUserNameCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789._-";
            options.Password.RequiredLength = 1;
            options.Password.RequireDigit = false;
            options.Password.RequireLowercase = false;
            options.Password.RequireUppercase = false;
            options.Password.RequireNonAlphanumeric = false;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
        }).AddEntityFrameworkStores<AppDbContext>().AddSignInManager().AddDefaultTokenProviders();
        builder.Services.AddScoped<IPasswordValidator<AppUser>, CodePointPasswordValidator>();
        builder.Services.AddSingleton<DatabaseTicketStore>();
        builder.Services.AddOptions<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme)
            .Configure<DatabaseTicketStore, TimeProvider>((options, store, clock) =>
            {
                options.SessionStore = store;
                options.TimeProvider = clock;
                options.Cookie.Name = "__Host-GoodsManager";
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.SlidingExpiration = false;
                options.ExpireTimeSpan = TimeSpan.FromDays(7);
                options.Events.OnValidatePrincipal = async context =>
                {
                    var manager = context.HttpContext.RequestServices.GetRequiredService<SignInManager<AppUser>>();
                    // 標準SecurityStampを毎要求で照合し、再発行による期限の延長を避ける
                    if (await manager.ValidateSecurityStampAsync(context.Principal) is null)
                    {
                        context.RejectPrincipal();
                        await manager.SignOutAsync();
                    }
                };
                options.Events.OnRedirectToLogin = context => WriteAuthProblemAsync(context.HttpContext, 401, "AUTHENTICATION_REQUIRED");
                options.Events.OnRedirectToAccessDenied = context => WriteAuthProblemAsync(context.HttpContext, 403, "FORBIDDEN");
            });
        builder.Services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-CSRF-Token";
            options.Cookie.Name = "__Host-GoodsManager-Csrf";
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        });
        builder.Services.AddAuthorization();
        var origin = builder.Configuration["Authentication:Origin"] ?? (builder.Environment.IsDevelopment() ? "https://localhost:7443" : null);
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var originUri) || originUri.Scheme != "https" || originUri.AbsolutePath != "/" || originUri.Query.Length > 0 || originUri.Fragment.Length > 0 || originUri.UserInfo.Length > 0)
        {
            throw new InvalidOperationException("Authentication:Originに公開HTTPSオリジンを指定する必要がある");
        }

        var rpId = builder.Configuration["Authentication:RpId"] ?? originUri.Host;
        if (rpId != originUri.Host)
        {
            throw new InvalidOperationException("RP IDと公開オリジンのホストを一致させる必要がある");
        }

        builder.Services.Configure<IdentityPasskeyOptions>(options =>
        {
            options.ServerDomain = rpId;
            options.UserVerificationRequirement = "required";
            options.AuthenticatorTimeout = TimeSpan.FromMinutes(5);
            options.ValidateOrigin = context => ValueTask.FromResult(!context.CrossOrigin && context.Origin == originUri.GetLeftPart(UriPartial.Authority));
        });
        builder.Services.AddOptions<CookieAuthenticationOptions>(IdentityConstants.TwoFactorUserIdScheme).Configure<TimeProvider>((options, clock) =>
        {
            options.Cookie.Name = "__Host-GoodsManager-PasskeyState";
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.HttpOnly = true;
            options.SlidingExpiration = false;
            options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
            options.TimeProvider = clock;
        });
    }

    /// <summary>認証エラーをHTML転送せず共通形式で返す</summary>
    private static Task WriteAuthProblemAsync(HttpContext context, int status, string code) =>
        Results.Problem(statusCode: status, title: "認証を確認してください", extensions: new Dictionary<string, object?> { ["code"] = code }).ExecuteAsync(context);
}

/// <summary>単一インスタンス内の認証更新を直列化</summary>
public sealed class AuthWriteGate
{
    public SemaphoreSlim Semaphore { get; } = new(1, 1);
}

/// <summary>UTF-16の長さに依存せず、合意済みのパスワード条件を検証</summary>
public sealed class CodePointPasswordValidator : IPasswordValidator<AppUser>
{
    /// <summary>パスワードを補正せず拒否</summary>
    public Task<IdentityResult> ValidateAsync(UserManager<AppUser> manager, AppUser user, string? password) =>
        Task.FromResult(StorageValues.IsValidText(password, 12, 128)
            ? IdentityResult.Success
            : IdentityResult.Failed(new IdentityError { Code = "PasswordLength", Description = "パスワードは12～128コードポイント、NUL不可" }));
}
