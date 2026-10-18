using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace GoodsManager.Web.Auth;

/// <summary>Identity標準の同時実行情報をAPIの強いETagへ対応付ける</summary>
public static class AuthenticationEtags
{
    /// <summary>アカウントの現在版を表す。独自の変更番号は保持しない</summary>
    public static string Account(AppUser user) => $"\"account:{user.Id:D}:{Stamp(user)}\"";

    /// <summary>パスキーと所有アカウントの現在版を表す</summary>
    public static string Passkey(AppUser user, byte[] credentialId) => $"\"passkey:{PasskeyCeremonies.CredentialId(credentialId)}:{Stamp(user)}\"";

    /// <summary>明示した現在版を要求し、省略は428、古い版・弱いETag・ワイルドカードは412</summary>
    public static int? Check(HttpRequest request, string current)
    {
        if (request.Headers.IfMatch.Count == 0 || string.IsNullOrWhiteSpace(request.Headers.IfMatch.ToString()))
        {
            return StatusCodes.Status428PreconditionRequired;
        }

        return request.Headers.IfMatch.Count == 1 && request.Headers.IfMatch[0] == current
            ? null : StatusCodes.Status412PreconditionFailed;
    }

    /// <summary>標準スタンプをヘッダーに安全な文字列へ変換</summary>
    private static string Stamp(AppUser user) => !string.IsNullOrEmpty(user.ConcurrencyStamp)
        ? WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(user.ConcurrencyStamp))
        : throw new InvalidDataException("Identityの同時実行情報が欠落");
}
