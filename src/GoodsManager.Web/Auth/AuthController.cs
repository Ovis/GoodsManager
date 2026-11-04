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
public sealed class AuthController(AppDbContext db, UserManager<AppUser> users, SignInManager<AppUser> signIn, AuthWriteGate gate, TimeProvider clock, AuthAudit audit) : ControllerBase
{
    /// <summary>ユーザー情報を公開せず、初期登録の要否だけを返す</summary>
    [HttpGet("setup/status")]
    public async Task<IActionResult> SetupStatus() => Ok(new { setupRequired = !await db.Users.AnyAsync() });

    /// <summary>匿名・認証済みそれぞれのCSRFトークンを発行</summary>
    [HttpGet("auth/csrf")]
    public IActionResult Csrf([FromServices] IAntiforgery antiforgery) => Ok(new { token = antiforgery.GetAndStoreTokens(HttpContext).RequestToken });

    /// <summary>単一制約とトランザクションで初期ユーザーの同時作成を防ぐ</summary>
    [HttpPost("setup")]
    public async Task<IActionResult> Setup(SetupRequest request, [FromServices] SetupToken setupToken)
    {
        if (!ValidLoginId(request.LoginId))
        {
            return Failure(400, "VALIDATION_FAILED", kind: AuthEvent.AUTH_SETUP_REJECTED);
        }

        AppUser user;
        await gate.Semaphore.WaitAsync(HttpContext.RequestAborted);
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            if (await db.Users.AnyAsync())
            {
                return Failure(409, "SETUP_COMPLETED", kind: AuthEvent.AUTH_SETUP_REJECTED);
            }

            if (!setupToken.Verify(request.SetupToken))
            {
                return Failure(403, "SETUP_TOKEN_INVALID", kind: AuthEvent.AUTH_SETUP_REJECTED);
            }

            user = new AppUser { Id = Guid.NewGuid(), UserName = request.LoginId, LockoutEnabled = true, IsAdministrator = true };

            var result = await users.CreateAsync(user, request.Password);
            if (!result.Succeeded)
            {
                return Failure(400, "VALIDATION_FAILED", kind: AuthEvent.AUTH_SETUP_REJECTED);
            }

            db.Add(new Collections.CollectionMember { CollectionId = Collections.CollectionSchema.DefaultId, UserId = user.Id, Role = "manager" });
            if (request.CreateSampleCollection)
            {
                // アカウント登録だけを先に確定し、画像や商品の準備失敗で登録を取り消さない。
                var now = clock.GetUtcNow();
                var sample = new Collections.CollectionSpace { Id = Guid.NewGuid(), Name = "サンプルコレクション", SampleStatus = "pending", CreatedAt = now, UpdatedAt = now };
                db.Add(sample);
                db.Add(new Collections.CollectionMember { CollectionId = sample.Id, UserId = user.Id, Role = "manager" });
                var settings = new Images.ImageSettings { CreatedAt = now, UpdatedAt = now };
                db.Add(settings);
                db.Entry(settings).Property("CollectionId").CurrentValue = sample.Id;
            }

            await db.SaveChangesAsync();
            await IncrementRevisionAsync();
            await transaction.CommitAsync();

            setupToken.Invalidate();
            audit.Record(AuthEvent.AUTH_SETUP_CREATED, user.Id);
            await IssueCookieAsync(user, false);

            return Created("/api/v1/auth/session", new
            {
                user = UserDto(user)
            });
        }
        finally
        {
            gate.Semaphore.Release();
        }

    }

    /// <summary>ハッシュ計算だけを共通ゲートの外へ移し、計算後に認証世代・ハッシュ・失敗回数を再照合する。</summary>
    [HttpPost("auth/password")]
    public async Task<IActionResult> PasswordLogin(LoginRequest request, [FromServices] PasswordProbe probe)
    {
        if (!StorageValues.IsValidText(request.Password, 0, 128))
        {
            return Failure(401, "INVALID_CREDENTIALS", kind: AuthEvent.AUTH_PASSWORD_FAILED);
        }

        AppUser? snapshot;
        await gate.Semaphore.WaitAsync(HttpContext.RequestAborted);
        try
        {
            db.ChangeTracker.Clear();
            snapshot = ValidLoginId(request.LoginId) ? await users.FindByNameAsync(request.LoginId) : null;
        }
        finally { gate.Semaphore.Release(); }

        // この区間はDBへ書き込まない。匿名IDでも既知IDでも同じ標準検証処理を使う。
        var verified = users.PasswordHasher.VerifyHashedPassword(snapshot ?? new AppUser(), snapshot?.PasswordHash ?? probe.Hash, request.Password);
        var upgraded = verified == PasswordVerificationResult.SuccessRehashNeeded && snapshot is not null
            ? users.PasswordHasher.HashPassword(snapshot, request.Password) : null;
        if (snapshot is null)
        {
            return Failure(401, "INVALID_CREDENTIALS", kind: AuthEvent.AUTH_PASSWORD_FAILED);
        }

        await gate.Semaphore.WaitAsync(HttpContext.RequestAborted);
        try
        {
            db.ChangeTracker.Clear();
            if (User.Identity?.IsAuthenticated == true && await AuthenticationState.GetCurrentUserAsync(db, signIn, User) is null)
            {
                return Failure(401, "INVALID_CREDENTIALS", snapshot?.Id, kind: AuthEvent.AUTH_PASSWORD_FAILED);
            }

            var user = await users.FindByIdAsync(snapshot.Id.ToString("D"));
            if (user is null || user.PasswordHash != snapshot.PasswordHash || user.SecurityStamp != snapshot.SecurityStamp || user.NormalizedUserName != snapshot.NormalizedUserName
                || !await signIn.CanSignInAsync(user) || await users.IsLockedOutAsync(user))
            {
                return Failure(401, "INVALID_CREDENTIALS", snapshot?.Id, kind: AuthEvent.AUTH_PASSWORD_FAILED);
            }

            if (verified == PasswordVerificationResult.Failed)
            {
                // 最新の失敗回数に対して直列に更新し、並行要求でロックを取りこぼさない。
                EnsureSuccess(await users.AccessFailedAsync(user));
                if (await users.IsLockedOutAsync(user))
                {
                    audit.Record(AuthEvent.AUTH_PASSWORD_LOCKED, user.Id);
                }

                return Failure(401, "INVALID_CREDENTIALS", snapshot?.Id, kind: AuthEvent.AUTH_PASSWORD_FAILED);
            }

            if (upgraded is not null)
            {
                user.PasswordHash = upgraded;
                EnsureSuccess(await users.UpdateAsync(user));
            }

            EnsureSuccess(await users.ResetAccessFailedCountAsync(user));

            // 失効との順序を確定するため、最終確認からチケット保存は同じゲートで保護する。
            var expiresAt = await IssueCookieAsync(user, request.RememberMe);
            audit.Record(AuthEvent.AUTH_PASSWORD_LOGIN, user.Id);

            return Ok(new
            {
                user = UserDto(user),
                expiresAt
            });
        }
        finally { gate.Semaphore.Release(); }
    }

    /// <summary>標準認証チケットの期限と保持設定を返す</summary>
    [Authorize]
    [HttpGet("auth/session")]
    public async Task<IActionResult> Session()
    {
        var ticket = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        var user = await users.GetUserAsync(User);
        Response.Headers.ETag = AuthenticationEtags.Account(user!);

        return Ok(new
        {
            user = UserDto(user!),
            expiresAt = ticket.Properties!.ExpiresUtc,
            persistent = ticket.Properties.IsPersistent
        });
    }

    /// <summary>標準の同時実行情報で古い変更を拒否し、ログインIDを変更</summary>
    [Authorize]
    [HttpPut("account")]
    public async Task<IActionResult> ChangeLoginId(ChangeLoginIdRequest request)
    {
        if (!ValidLoginId(request.LoginId))
        {
            return Failure(400, "VALIDATION_FAILED");
        }

        await gate.Semaphore.WaitAsync(HttpContext.RequestAborted);
        try
        {
            var user = await AuthenticationState.GetCurrentUserAsync(db, signIn, User);
            if (user is null)
            {
                return Failure(401, "SESSION_EXPIRED");
            }

            if (AuthenticationEtags.Check(Request, AuthenticationEtags.Account(user)) is { } status)
            {
                return Failure(status, status == 428 ? "PRECONDITION_REQUIRED" : "REVISION_MISMATCH");
            }

            if (user.UserName != request.LoginId)
            {
                await using var transaction = await db.Database.BeginTransactionAsync();
                // ID編集では認証世代を変更せず、標準の正規化・検証・同時実行制御で保存する
                user.UserName = request.LoginId;
                EnsureSuccess(await users.UpdateAsync(user));
                await IncrementRevisionAsync();
                await transaction.CommitAsync();
                audit.Record(AuthEvent.AUTH_LOGIN_ID_CHANGED, user.Id);
            }

            Response.Headers.ETag = AuthenticationEtags.Account(user);

            return Ok(new
            {
                user = UserDto(user)
            });
        }
        finally
        {
            gate.Semaphore.Release();
        }
    }

    /// <summary>現在の標準チケットとクッキーを削除</summary>
    [Authorize]
    [HttpPost("auth/logout")]
    public async Task<IActionResult> Logout()
    {
        await gate.Semaphore.WaitAsync(HttpContext.RequestAborted);
        try
        {
            var userId = AuthAudit.UserId(User);
            await signIn.SignOutAsync();
            audit.Record(AuthEvent.AUTH_LOGOUT, userId);

            return NoContent();
        }
        finally
        {
            gate.Semaphore.Release();
        }
    }

    /// <summary>標準SecurityStampの更新で全端末を失効</summary>
    [Authorize]
    [HttpPost("auth/revoke-all")]
    public async Task<IActionResult> RevokeAll()
    {
        await gate.Semaphore.WaitAsync(HttpContext.RequestAborted);
        try
        {
            var user = await AuthenticationState.GetCurrentUserAsync(db, signIn, User);
            if (user is null)
            {
                return Failure(401, "SESSION_EXPIRED");
            }

            await using var transaction = await db.Database.BeginTransactionAsync();
            EnsureSuccess(await users.UpdateSecurityStampAsync(user));
            await db.AuthTickets.Where(x => x.UserId == user.Id).ExecuteDeleteAsync();
            await IncrementRevisionAsync();
            await transaction.CommitAsync();

            audit.Record(AuthEvent.AUTH_ALL_SESSIONS_REVOKED, user.Id);
            await signIn.SignOutAsync();

            return NoContent();
        }
        finally
        {
            gate.Semaphore.Release();
        }

    }

    /// <summary>同じ要求のパスワードまたは対象操作用の既存パスキーで本人確認して変更</summary>
    [Authorize]
    [HttpPost("account/password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, [FromServices] OperationVerification verification)
    {
        await gate.Semaphore.WaitAsync(HttpContext.RequestAborted);
        try
        {
            var user = await AuthenticationState.GetCurrentUserAsync(db, signIn, User);
            if (user is null)
            {
                return Failure(401, "SESSION_EXPIRED");
            }

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

            await db.AuthTickets.Where(x => x.UserId == user.Id).ExecuteDeleteAsync();
            await IncrementRevisionAsync();
            await transaction.CommitAsync();
            audit.Record(AuthEvent.AUTH_PASSWORD_CHANGED, user.Id);
            await signIn.SignOutAsync();

            return NoContent();
        }
        finally
        {
            gate.Semaphore.Release();
        }

    }

    /// <summary>保持の有無による絶対期限を設定。自動延長は許可しない</summary>
    internal async Task<DateTimeOffset> IssueCookieAsync(AppUser user, bool rememberMe)
    {
        var now = clock.GetUtcNow();
        var expires = now.AddDays(rememberMe ? 30 : 7);
        var properties = new AuthenticationProperties { IsPersistent = rememberMe, IssuedUtc = now, ExpiresUtc = expires, AllowRefresh = false };
        await signIn.SignInAsync(user, properties);
        Response.Headers.ETag = AuthenticationEtags.Account(user);

        // 標準チケットが採用した精度の期限を返し、表示上の期限と食い違わせない
        return properties.ExpiresUtc!.Value;
    }

    /// <summary>保全対象の認証変更だけ変更番号を進める</summary>
    internal Task IncrementRevisionAsync() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE SystemState SET DataRevision=DataRevision+1, UpdatedAt={StorageValues.ToMilliseconds(clock.GetUtcNow())} WHERE Id=1");

    /// <summary>合意済みのログインID形式を検査</summary>
    internal static bool ValidLoginId(string? id) => id is { Length: >= 1 and <= 64 } && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');

    /// <summary>秘密や標準エンティティを公開しないユーザー情報</summary>
    internal static object UserDto(AppUser user) => new { id = user.Id, loginId = user.UserName, user.IsAdministrator, etag = AuthenticationEtags.Account(user) };

    /// <summary>認証処理のエラーを共通形式で返す</summary>
    internal ObjectResult Failure(int status, string code, Guid? userId = null, AuthEvent kind = AuthEvent.AUTH_CHANGE_REJECTED)
    {
        audit.Failure(HttpContext, kind, userId);

        return Problem(statusCode: status, title: "認証処理を確認してください", extensions: new Dictionary<string, object?> { ["code"] = code });
    }

    /// <summary>標準ストアの保存失敗を成功扱いしない</summary>
    internal static void EnsureSuccess(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException("Identityの保存に失敗");
        }
    }
}

/// <summary>最初のアカウントと、任意の独立した試用コレクションを作る入力。</summary>
public sealed record SetupRequest(string LoginId, string Password, string? SetupToken, bool CreateSampleCollection = false);
public sealed record LoginRequest(string LoginId, string Password, bool RememberMe);
public sealed record VerificationRequest(string? Password, Guid? ChallengeId, System.Text.Json.JsonElement? Credential);
public sealed record ChangePasswordRequest(string NewPassword, VerificationRequest Verification);
public sealed record ChangeLoginIdRequest(string LoginId);
