using System.Security.Claims;
using GoodsManager.Web.Storage;
using Microsoft.AspNetCore.Identity;

namespace GoodsManager.Web.Auth;

/// <summary>更新待機後の認証世代を、標準Identityで再確認</summary>
public static class AuthenticationState
{
    /// <summary>書込ゲート取得直後・変更開始前に呼び、失効前の追跡ユーザーを再利用しない</summary>
    public static Task<AppUser?> GetCurrentUserAsync(AppDbContext database, SignInManager<AppUser> signIn, ClaimsPrincipal principal)
    {
        // クッキー検証で読み込んだユーザーが、待機中のパスワード変更で古くなる場合がある
        database.ChangeTracker.Clear();
        return signIn.ValidateSecurityStampAsync(principal);
    }
}
