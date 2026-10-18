using Microsoft.AspNetCore.Identity;

namespace GoodsManager.Web.Auth;

/// <summary>対象の管理操作と同じ要求で本人確認する。直近認証の汎用期限は持たない</summary>
public sealed class OperationVerification(SignInManager<AppUser> signIn, UserManager<AppUser> users, PasskeyCeremonies ceremonies)
{
    /// <summary>パスワードを標準Identityで検証</summary>
    public async Task<bool> VerifyAsync(AppUser user, VerificationRequest request, string purpose, HttpContext context)
    {
        if (request.Password is not null)
        {
            return Storage.StorageValues.IsValidText(request.Password, 1, 128)
                && (await signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true)).Succeeded;
        }

        if (request.ChallengeId is not { } id || request.Credential is not { } credential
            || ceremonies.Consume(context, id, purpose, user) is null || await users.IsLockedOutAsync(user))
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
