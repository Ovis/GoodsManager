using Microsoft.AspNetCore.Identity;

namespace GoodsManager.Web.Auth;

/// <summary>対象の管理操作と同じ要求で本人確認する。直近認証の汎用期限は持たない</summary>
public sealed class OperationVerification(SignInManager<AppUser> signIn)
{
    /// <summary>パスワードを標準Identityで検証</summary>
    public async Task<bool> VerifyAsync(AppUser user, VerificationRequest request, string purpose, HttpContext context)
    {
        return request.Password is not null && (await signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true)).Succeeded;
    }
}
