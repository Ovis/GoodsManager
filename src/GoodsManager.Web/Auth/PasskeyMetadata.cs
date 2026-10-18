using GoodsManager.Web.Storage;
using Microsoft.AspNetCore.Identity;

namespace GoodsManager.Web.Auth;

/// <summary>標準Identityの型付きメタデータを保存・読取の両側で検査</summary>
public static class PasskeyMetadata
{
    /// <summary>JSON構文が正しくても不正な内容を既定値で受け入れない</summary>
    public static UserPasskeyInfo RequireValid(UserPasskeyInfo value)
    {
        if (value.CredentialId.Length is < 1 or > 1023 || value.PublicKey.Length == 0
            || value.AttestationObject.Length == 0 || value.ClientDataJson.Length == 0
            || !StorageValues.IsValidText(value.Name, 1, 100)
            || value.Transports?.Any(transport => !StorageValues.IsValidText(transport, 0, int.MaxValue)) == true)
        {
            throw new InvalidDataException("パスキーのメタデータが不正");
        }

        return value;
    }
}
