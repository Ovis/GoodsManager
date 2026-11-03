using Microsoft.AspNetCore.Identity;

namespace GoodsManager.Web.Auth;

/// <summary>対象の管理操作と同じ要求で本人確認する。直近認証の汎用期限は持たない</summary>
public sealed class OperationVerification(SignInManager<AppUser> signIn, UserManager<AppUser> users, PasskeyCeremonies ceremonies, AuthAudit audit)
{
    /// <summary>パスワードまたは対象操作用のパスキーで本人確認し、失敗とロック遷移を監査する。</summary>
    /// <remarks>ユーザーの再取得から検証まで、呼び出し元でAuthWriteGateを保持する。</remarks>
    public async Task<bool> VerifyAsync(AppUser user, VerificationRequest request, string purpose, HttpContext context)
    {
        if (request.Password is not null)
        {
            // 呼び出し元の認証書き込みゲート内で、ロック前後を同じユーザーに対して比較する。
            var wasLocked = await users.IsLockedOutAsync(user);
            var result = Storage.StorageValues.IsValidText(request.Password, 1, 128)
                ? await signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true)
                : SignInResult.Failed;
            if (!result.Succeeded)
            {
                audit.Failure(context, AuthEvent.AUTH_PASSWORD_VERIFICATION_FAILED, user.Id);
                if (!wasLocked && result.IsLockedOut)
                {
                    audit.Record(AuthEvent.AUTH_PASSWORD_LOCKED, user.Id);
                }
            }

            return result.Succeeded;
        }

        if (request.ChallengeId is not { } id || request.Credential is not { } credential
            || ceremonies.Consume(context, id, purpose, user) is null)
        {
            return false;
        }

        try
        {
            var result = await signIn.PerformPasskeyAssertionAsync(credential.GetRawText());
            if (!result.Succeeded || result.User.Id != user.Id)
            {
                return false;
            }

            AuthController.EnsureSuccess(await users.AddOrUpdatePasskeyAsync(user, PasskeyMetadata.RequireValid(result.Passkey)));

            return true;
        }
        catch (InvalidOperationException)
        {
            // 期限切れ・消費済みの標準一時クッキーを管理操作の許可に使わない
            return false;
        }
    }
}
