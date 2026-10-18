using GoodsManager.Web.Storage;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GoodsManager.Web.Auth;

/// <summary>初期セットアップと標準Identityを使ったパスワード認証</summary>
[ApiController]
[Route("api/v1")]
public sealed class AuthController(AppDbContext db, UserManager<AppUser> users, SignInManager<AppUser> signIn, AuthWriteGate gate, TimeProvider clock) : ControllerBase
{
    /// <summary>ユーザー情報を公開せず、初期登録の要否だけを返す</summary>
    [HttpGet("setup/status")]
    public async Task<IActionResult> SetupStatus() => Ok(new { setupRequired = !await db.Users.AnyAsync() });

    /// <summary>匿名・認証済みそれぞれのCSRFトークンを発行</summary>
    [HttpGet("auth/csrf")]
    public IActionResult Csrf([FromServices] IAntiforgery antiforgery) => Ok(new { token = antiforgery.GetAndStoreTokens(HttpContext).RequestToken });

    /// <summary>単一制約とトランザクションで初期ユーザーの同時作成を防ぐ</summary>
    [HttpPost("setup")]
    public async Task<IActionResult> Setup(SetupRequest request)
    {
        if (!ValidLoginId(request.LoginId))
        {
            return Failure(400, "VALIDATION_FAILED");
        }

        AppUser user;
        await gate.Semaphore.WaitAsync(HttpContext.RequestAborted);
        try
        {
            if (await db.Users.AnyAsync())
            {
                return Failure(409, "SETUP_COMPLETED");
            }

            await using var transaction = await db.Database.BeginTransactionAsync();
            user = new AppUser { Id = Guid.NewGuid(), UserName = request.LoginId, LockoutEnabled = true };
            var result = await users.CreateAsync(user, request.Password);
            if (!result.Succeeded)
            {
                return Failure(400, "VALIDATION_FAILED");
            }

            await IncrementRevisionAsync();
            await transaction.CommitAsync();
        }
        finally
        {
            gate.Semaphore.Release();
        }

        await IssueCookieAsync(user, false);
        return Created("/api/v1/auth/session", new { user = UserDto(user) });
    }

    /// <summary>失敗回数の更新を直列化し、存在しないIDと誤パスワードを同じ応答にする</summary>
    [HttpPost("auth/password")]
    public async Task<IActionResult> PasswordLogin(LoginRequest request)
    {
        AppUser? user;
        await gate.Semaphore.WaitAsync(HttpContext.RequestAborted);
        try
        {
            user = ValidLoginId(request.LoginId) ? await users.FindByNameAsync(request.LoginId) : null;
            if (user is null)
            {
                // IDの有無によるハッシュ検証時間の大きな差を抑える
                var dummy = new AppUser();
                users.PasswordHasher.HashPassword(dummy, request.Password);
                return Failure(401, "INVALID_CREDENTIALS");
            }

            if (!StorageValues.IsValidText(request.Password, 12, 128)
                || !(await signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true)).Succeeded)
            {
                return Failure(401, "INVALID_CREDENTIALS");
            }
        }
        finally
        {
            gate.Semaphore.Release();
        }

        var expiresAt = await IssueCookieAsync(user, request.RememberMe);
        return Ok(new { user = UserDto(user), expiresAt });
    }

    /// <summary>標準認証チケットの期限と保持設定を返す</summary>
    [Authorize]
    [HttpGet("auth/session")]
    public async Task<IActionResult> Session()
    {
        var ticket = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        var user = await users.GetUserAsync(User);
        return Ok(new { user = UserDto(user!), expiresAt = ticket.Properties!.ExpiresUtc, persistent = ticket.Properties.IsPersistent });
    }

    /// <summary>現在の標準チケットとクッキーを削除</summary>
    [Authorize]
    [HttpPost("auth/logout")]
    public async Task<IActionResult> Logout()
    {
        await signIn.SignOutAsync();
        return NoContent();
    }

    /// <summary>標準SecurityStampの更新で全端末を失効</summary>
    [Authorize]
    [HttpPost("auth/revoke-all")]
    public async Task<IActionResult> RevokeAll()
    {
        await gate.Semaphore.WaitAsync(HttpContext.RequestAborted);
        try
        {
            var user = (await users.GetUserAsync(User))!;
            await using var transaction = await db.Database.BeginTransactionAsync();
            EnsureSuccess(await users.UpdateSecurityStampAsync(user));
            await db.AuthTickets.ExecuteDeleteAsync();
            await IncrementRevisionAsync();
            await transaction.CommitAsync();
        }
        finally
        {
            gate.Semaphore.Release();
        }

        await signIn.SignOutAsync();
        return NoContent();
    }

    /// <summary>同じ要求のパスワードまたは対象操作用の既存パスキーで本人確認して変更</summary>
    [Authorize]
    [HttpPost("account/password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, [FromServices] OperationVerification verification)
    {
        await gate.Semaphore.WaitAsync(HttpContext.RequestAborted);
        try
        {
            var user = (await users.GetUserAsync(User))!;
            if (!await verification.VerifyAsync(user, request.Verification, "password", HttpContext))
            {
                return Failure(403, "REAUTHENTICATION_REQUIRED");
            }

            await using var transaction = await db.Database.BeginTransactionAsync();
            // 本人確認済みの要求内だけで標準のリセットトークンを発行・消費する
            var token = await users.GeneratePasswordResetTokenAsync(user);
            var result = await users.ResetPasswordAsync(user, token, request.NewPassword);
            if (!result.Succeeded)
            {
                return Failure(400, "VALIDATION_FAILED");
            }

            await db.AuthTickets.ExecuteDeleteAsync();
            await IncrementRevisionAsync();
            await transaction.CommitAsync();
        }
        finally
        {
            gate.Semaphore.Release();
        }

        await signIn.SignOutAsync();
        return NoContent();
    }

    /// <summary>保持の有無による絶対期限を設定。自動延長は許可しない</summary>
    internal async Task<DateTimeOffset> IssueCookieAsync(AppUser user, bool rememberMe)
    {
        var now = clock.GetUtcNow();
        var expires = now.AddDays(rememberMe ? 30 : 7);
        await signIn.SignInAsync(user, new AuthenticationProperties { IsPersistent = rememberMe, IssuedUtc = now, ExpiresUtc = expires, AllowRefresh = false });
        return expires;
    }

    /// <summary>保全対象の認証変更だけ変更番号を進める</summary>
    internal Task IncrementRevisionAsync() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE SystemState SET DataRevision=DataRevision+1, UpdatedAt={StorageValues.ToMilliseconds(clock.GetUtcNow())} WHERE Id=1");

    /// <summary>合意済みのログインID形式を検査</summary>
    internal static bool ValidLoginId(string? id) => id is { Length: >= 1 and <= 64 } && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');

    /// <summary>秘密や標準エンティティを公開しないユーザー情報</summary>
    internal static object UserDto(AppUser user) => new { id = user.Id, loginId = user.UserName };

    /// <summary>認証処理のエラーを共通形式で返す</summary>
    internal ObjectResult Failure(int status, string code) => Problem(statusCode: status, title: "認証処理を確認してください", extensions: new Dictionary<string, object?> { ["code"] = code });

    /// <summary>標準ストアの保存失敗を成功扱いしない</summary>
    internal static void EnsureSuccess(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException("Identityの保存に失敗");
        }
    }
}

public sealed record SetupRequest(string LoginId, string Password);
public sealed record LoginRequest(string LoginId, string Password, bool RememberMe);
public sealed record VerificationRequest(string? Password, Guid? ChallengeId, System.Text.Json.JsonElement? Credential);
public sealed record ChangePasswordRequest(string NewPassword, VerificationRequest Verification);
