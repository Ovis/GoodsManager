using System.Text.Json;
using GoodsManager.Web.Storage;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace GoodsManager.Web.Auth;

/// <summary>Identity標準の暗号検証とストアを使用するパスキーAPI</summary>
[ApiController]
[Route("api/v1")]
public sealed class PasskeysController(AppDbContext db, UserManager<AppUser> users, SignInManager<AppUser> signIn, PasskeyCeremonies ceremonies, OperationVerification verification, AuthWriteGate gate, TimeProvider clock) : ControllerBase
{
    /// <summary>ログイン保持をチャレンジに結び付け、標準の認証選択肢を発行</summary>
    [HttpPost("auth/passkey/options")]
    public async Task<IActionResult> LoginOptions(PasskeyLoginOptions request)
    {
        var user = request.LoginId is not null && AuthController.ValidLoginId(request.LoginId) ? await users.FindByNameAsync(request.LoginId) : null;
        var options = await signIn.MakePasskeyRequestOptionsAsync(user);
        // ユーザー名なしの認証でも、発行時の認証世代へ結び付けて復旧前の処理を拒否
        var bindingUser = user ?? await db.Users.SingleOrDefaultAsync();
        return Options(ceremonies.Begin(HttpContext, "login", bindingUser, request.RememberMe), options);
    }

    /// <summary>標準の署名検証とカウンター更新後、合意済みの絶対期限でログイン</summary>
    [HttpPost("auth/passkey/verify")]
    public async Task<IActionResult> Login(PasskeyCredentialRequest request)
    {
        var ceremony = ceremonies.Consume(HttpContext, request.ChallengeId, "login");
        if (ceremony is null)
        {
            return Failure(400, "PASSKEY_CHALLENGE_INVALID");
        }

        AppUser user;
        await gate.Semaphore.WaitAsync(HttpContext.RequestAborted);
        try
        {
            db.ChangeTracker.Clear();
            var result = await AssertionAsync(request.Credential);
            if (result is null || !result.Succeeded || await users.IsLockedOutAsync(result.User)
                || (ceremony.UserId.HasValue && (ceremony.UserId != result.User.Id || ceremony.SecurityStamp != result.User.SecurityStamp)))
            {
                return Failure(401, "INVALID_CREDENTIALS");
            }

            user = result.User;
            AuthController.EnsureSuccess(await users.AddOrUpdatePasskeyAsync(user, PasskeyMetadata.RequireValid(result.Passkey)));

            var now = clock.GetUtcNow();
            var expires = now.AddDays(ceremony.RememberMe ? 30 : 7);
            var properties = new AuthenticationProperties { IsPersistent = ceremony.RememberMe, IssuedUtc = now, ExpiresUtc = expires, AllowRefresh = false };
            await signIn.SignInAsync(user, properties);
            return Ok(new { user = AuthController.UserDto(user), expiresAt = properties.ExpiresUtc });
        }
        finally
        {
            gate.Semaphore.Release();
        }

    }

    /// <summary>パスワード変更・パスキー追加/削除の対象だけ本人確認選択肢を発行</summary>
    [Authorize]
    [HttpPost("auth/reauthenticate")]
    public async Task<IActionResult> VerificationOptions(VerificationOptionsRequest request)
    {
        if (request.Purpose is not ("password" or "passkey-add") && !request.Purpose.StartsWith("passkey-delete:", StringComparison.Ordinal))
        {
            return Failure(400, "VALIDATION_FAILED");
        }

        var user = (await users.GetUserAsync(User))!;
        var options = await signIn.MakePasskeyRequestOptionsAsync(user);
        return Options(ceremonies.Begin(HttpContext, request.Purpose, user), options);
    }

    /// <summary>公開鍵や認証データを含めずパスキー一覧を返す</summary>
    [Authorize]
    [HttpGet("account/passkeys")]
    public async Task<IActionResult> List()
    {
        var user = (await users.GetUserAsync(User))!;
        // 応答を書き始める前に全件検証し、破損時に途中までの200応答を返さない
        return Ok((await users.GetPasskeysAsync(user)).Select(passkey => Dto(PasskeyMetadata.RequireValid(passkey), user)).ToArray());
    }

    /// <summary>対象操作の本人確認後に登録選択肢を発行</summary>
    [Authorize]
    [HttpPost("account/passkeys/options")]
    public async Task<IActionResult> RegistrationOptions(RegistrationOptionsRequest request)
    {
        await gate.Semaphore.WaitAsync(HttpContext.RequestAborted);
        try
        {
            var user = await AuthenticationState.GetCurrentUserAsync(db, signIn, User);
            if (user is null)
            {
                return Failure(401, "SESSION_EXPIRED");
            }

            if (!await verification.VerifyAsync(user, request.Verification, "passkey-add", HttpContext))
            {
                return Failure(403, "REAUTHENTICATION_REQUIRED");
            }

            var options = await signIn.MakePasskeyCreationOptionsAsync(new PasskeyUserEntity { Id = user.Id.ToString("D"), Name = user.UserName!, DisplayName = user.UserName! });
            return Options(ceremonies.Begin(HttpContext, "register", user), options);
        }
        finally
        {
            gate.Semaphore.Release();
        }
    }

    /// <summary>標準の登録検証結果の所有者を照合し、既存パスキーを上書きせず保存</summary>
    [Authorize]
    [HttpPost("account/passkeys")]
    public async Task<IActionResult> Register(RegisterPasskeyRequest request)
    {
        await gate.Semaphore.WaitAsync(HttpContext.RequestAborted);
        try
        {
            var user = await AuthenticationState.GetCurrentUserAsync(db, signIn, User);
            if (user is null)
            {
                return Failure(401, "SESSION_EXPIRED");
            }

            if (ceremonies.Consume(HttpContext, request.ChallengeId, "register", user) is null)
            {
                return Failure(400, "PASSKEY_CHALLENGE_INVALID");
            }

            if (!StorageValues.IsValidText(request.DisplayName, 1, 100))
            {
                return Failure(400, "VALIDATION_FAILED");
            }

            PasskeyAttestationResult result;
            try
            {
                result = await signIn.PerformPasskeyAttestationAsync(request.Credential.GetRawText());
            }
            catch (InvalidOperationException)
            {
                return Failure(400, "PASSKEY_CHALLENGE_INVALID");
            }

            if (!result.Succeeded || result.UserEntity.Id != user.Id.ToString("D"))
            {
                return Failure(400, "PASSKEY_INVALID");
            }

            if ((await users.GetPasskeysAsync(user)).Any(x => x.CredentialId.SequenceEqual(result.Passkey.CredentialId)))
            {
                return Failure(409, "PASSKEY_EXISTS");
            }

            await using var transaction = await db.Database.BeginTransactionAsync();
            result.Passkey.Name = request.DisplayName;
            AuthController.EnsureSuccess(await users.AddOrUpdatePasskeyAsync(user, PasskeyMetadata.RequireValid(result.Passkey)));
            await IncrementRevisionAsync();
            await transaction.CommitAsync();
            Response.Headers.ETag = AuthenticationEtags.Passkey(user, result.Passkey.CredentialId);
            return Created("/api/v1/account/passkeys/" + PasskeyCeremonies.CredentialId(result.Passkey.CredentialId), Dto(result.Passkey, user));
        }
        finally
        {
            gate.Semaphore.Release();
        }
    }

    /// <summary>表示名だけを変更。追加の本人確認は要求しない</summary>
    [Authorize]
    [HttpPut("account/passkeys/{id}")]
    public async Task<IActionResult> Rename(string id, RenamePasskeyRequest request)
    {
        if (!StorageValues.IsValidText(request.DisplayName, 1, 100))
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

            var passkey = (await users.GetPasskeysAsync(user)).SingleOrDefault(x => PasskeyCeremonies.CredentialId(x.CredentialId) == id);
            if (passkey is null)
            {
                return NotFound();
            }

            if (AuthenticationEtags.Check(Request, AuthenticationEtags.Passkey(user, passkey.CredentialId)) is { } status)
            {
                return Failure(status, status == 428 ? "PRECONDITION_REQUIRED" : "REVISION_MISMATCH");
            }

            await using var transaction = await db.Database.BeginTransactionAsync();
            PasskeyMetadata.RequireValid(passkey);
            passkey.Name = request.DisplayName;
            AuthController.EnsureSuccess(await users.AddOrUpdatePasskeyAsync(user, passkey));
            await IncrementRevisionAsync();
            await transaction.CommitAsync();
            Response.Headers.ETag = AuthenticationEtags.Passkey(user, passkey.CredentialId);
            return Ok(Dto(passkey, user));
        }
        finally
        {
            gate.Semaphore.Release();
        }
    }

    /// <summary>削除対象と結び付いた本人確認後に削除</summary>
    [Authorize]
    [HttpDelete("account/passkeys/{id}")]
    public async Task<IActionResult> Delete(string id, [FromBody] VerificationRequest request)
    {
        await gate.Semaphore.WaitAsync(HttpContext.RequestAborted);
        try
        {
            var user = await AuthenticationState.GetCurrentUserAsync(db, signIn, User);
            if (user is null)
            {
                return Failure(401, "SESSION_EXPIRED");
            }

            var passkey = (await users.GetPasskeysAsync(user)).SingleOrDefault(x => PasskeyCeremonies.CredentialId(x.CredentialId) == id);
            if (passkey is null)
            {
                return NotFound();
            }

            if (AuthenticationEtags.Check(Request, AuthenticationEtags.Passkey(user, passkey.CredentialId)) is { } status)
            {
                return Failure(status, status == 428 ? "PRECONDITION_REQUIRED" : "REVISION_MISMATCH");
            }

            // 署名カウンターの更新で標準スタンプも変わるため、版の照合は本人確認の前に行う
            if (!await verification.VerifyAsync(user, request, "passkey-delete:" + id, HttpContext))
            {
                return Failure(403, "REAUTHENTICATION_REQUIRED");
            }

            await using var transaction = await db.Database.BeginTransactionAsync();
            AuthController.EnsureSuccess(await users.RemovePasskeyAsync(user, passkey.CredentialId));
            await IncrementRevisionAsync();
            await transaction.CommitAsync();
            return NoContent();
        }
        finally
        {
            gate.Semaphore.Release();
        }
    }

    /// <summary>標準一時クッキーの失効・欠落を認証失敗に変換</summary>
    private async Task<PasskeyAssertionResult<AppUser>?> AssertionAsync(JsonElement credential)
    {
        try
        {
            return await signIn.PerformPasskeyAssertionAsync(credential.GetRawText());
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Identity標準JSONを改変せずブラウザ用DTOへ載せる</summary>
    private OkObjectResult Options(Guid id, string json) => Ok(new { challengeId = id, publicKeyOptions = JsonSerializer.Deserialize<JsonElement>(json) });

    /// <summary>表示用情報だけを返す。認証IDはUUIDと混同しない</summary>
    private static object Dto(UserPasskeyInfo passkey, AppUser user) => new { id = PasskeyCeremonies.CredentialId(passkey.CredentialId), displayName = passkey.Name, createdAt = passkey.CreatedAt, etag = AuthenticationEtags.Passkey(user, passkey.CredentialId) };

    /// <summary>認証設定の変更を保全対象へ反映</summary>
    private Task IncrementRevisionAsync() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE SystemState SET DataRevision=DataRevision+1, UpdatedAt={StorageValues.ToMilliseconds(clock.GetUtcNow())} WHERE Id=1");

    /// <summary>秘密を含まない失敗応答</summary>
    private ObjectResult Failure(int status, string code) => Problem(statusCode: status, title: "パスキーの操作を確認してください", extensions: new Dictionary<string, object?> { ["code"] = code });
}

public sealed record PasskeyLoginOptions(string? LoginId, bool RememberMe);
public sealed record PasskeyCredentialRequest(Guid ChallengeId, JsonElement Credential);
public sealed record VerificationOptionsRequest(string Purpose);
public sealed record RegistrationOptionsRequest(VerificationRequest Verification);
public sealed record RegisterPasskeyRequest(Guid ChallengeId, JsonElement Credential, string DisplayName);
public sealed record RenamePasskeyRequest(string DisplayName);
