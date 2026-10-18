using Microsoft.AspNetCore.Identity;

namespace GoodsManager.Web.Development;

/// <summary>例外本文や認証データを公開せず、開発時の拒否理由を分類</summary>
public static class PasskeyDiagnostics
{
    /// <summary>標準ハンドラーの既知の失敗を固定コードへ変換</summary>
    public static string Classify(PasskeyException? failure)
    {
        var message = failure?.Message ?? "";
        foreach (var (prefix, code) in new (string, string)[]
        {
            ("The authenticator response challenge", "CHALLENGE_MISMATCH"),
            ("The authenticator response had an invalid origin", "ORIGIN_MISMATCH"),
            ("The authenticator data included an invalid Relying Party", "RP_ID_MISMATCH"),
            ("The authenticator data flags did not include", "USER_NOT_PRESENT"),
            ("User verification is required", "USER_NOT_VERIFIED"),
            ("The credential is backed up", "BACKUP_FLAGS_INVALID"),
            ("The credential public key algorithm", "ALGORITHM_UNSUPPORTED"),
            ("The credential public key was invalid", "PUBLIC_KEY_INVALID"),
            ("The provided credential ID does not match", "CREDENTIAL_ID_MISMATCH"),
            ("The credential is already registered", "CREDENTIAL_EXISTS"),
            ("The attestation credential JSON", "CREDENTIAL_JSON_INVALID"),
            ("The client data JSON", "CLIENT_DATA_JSON_INVALID"),
            ("Expected the client data JSON", "CLIENT_DATA_TYPE_INVALID"),
            ("The attestation object", "ATTESTATION_OBJECT_INVALID"),
            ("An exception occurred while parsing the attestation object", "ATTESTATION_OBJECT_INVALID"),
            ("The authenticator data had", "AUTHENTICATOR_DATA_INVALID"),
            ("The attested credential data had", "CREDENTIAL_DATA_INVALID"),
            ("The attestation state JSON", "ATTESTATION_STATE_INVALID"),
            ("An unexpected error occurred", "HANDLER_EXCEPTION")
        })
        {
            if (message.StartsWith(prefix, StringComparison.Ordinal))
            {
                return code;
            }
        }

        return "ATTESTATION_REJECTED";
    }
}
