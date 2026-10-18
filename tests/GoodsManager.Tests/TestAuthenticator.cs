using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace GoodsManager.Tests;

/// <summary>実署名を作る試験用WebAuthn認証器。ブラウザや実機の代替受入には使わない</summary>
public sealed class TestAuthenticator : IDisposable
{
    private readonly ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly byte[] credentialId = RandomNumberGenerator.GetBytes(32);
    private byte[] userHandle = [];
    private uint count;
    public string Id => WebEncoders.Base64UrlEncode(credentialId);

    /// <summary>none形式の登録応答を作り、Identity標準の検証へ渡す</summary>
    public JsonElement Create(JsonElement options, string origin = "https://localhost:7443", string rpId = "localhost", string? challenge = null, bool verified = true)
    {
        userHandle = WebEncoders.Base64UrlDecode(options.GetProperty("user").GetProperty("id").GetString()!);
        var clientData = ClientData("webauthn.create", challenge ?? options.GetProperty("challenge").GetString()!, origin);
        var parameters = key.ExportParameters(false);
        byte[] cose = [0xa5, 0x01, 0x02, 0x03, 0x26, 0x20, 0x01, 0x21, 0x58, 0x20, .. parameters.Q.X!, 0x22, 0x58, 0x20, .. parameters.Q.Y!];
        byte[] authData = [.. AuthenticatorData(rpId, (byte)(verified ? 0x45 : 0x41), 0), .. new byte[16], 0, 32, .. credentialId, .. cose];
        using var stream = new MemoryStream();
        stream.WriteByte(0xa3);
        WriteText(stream, "fmt");
        WriteText(stream, "none");
        WriteText(stream, "attStmt");
        stream.WriteByte(0xa0);
        WriteText(stream, "authData");
        WriteBytes(stream, authData);
        return JsonSerializer.SerializeToElement(new
        {
            id = Id,
            rawId = Id,
            type = "public-key",
            authenticatorAttachment = "platform",
            clientExtensionResults = new { },
            response = new { clientDataJSON = Encode(clientData), attestationObject = Encode(stream.ToArray()), transports = new[] { "internal" } }
        });
    }

    /// <summary>認証・本人確認用のECDSA署名を作る</summary>
    public JsonElement Get(JsonElement options, string origin = "https://localhost:7443", bool badSignature = false)
    {
        var data = ClientData("webauthn.get", options.GetProperty("challenge").GetString()!, origin);
        var auth = AuthenticatorData("localhost", 0x05, ++count);
        var signature = key.SignData([.. auth, .. SHA256.HashData(data)], HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        if (badSignature)
        {
            signature[^1] ^= 1;
        }

        return JsonSerializer.SerializeToElement(new
        {
            id = Id,
            rawId = Id,
            type = "public-key",
            authenticatorAttachment = "platform",
            clientExtensionResults = new { },
            response = new { clientDataJSON = Encode(data), authenticatorData = Encode(auth), signature = Encode(signature), userHandle = Encode(userHandle) }
        });
    }

    /// <summary>WebAuthnのクライアント情報をUTF-8で作成</summary>
    private static byte[] ClientData(string type, string challenge, string origin) => JsonSerializer.SerializeToUtf8Bytes(new { type, challenge, origin, crossOrigin = false });

    /// <summary>RPハッシュ・フラグ・署名カウンターを作成</summary>
    private static byte[] AuthenticatorData(string rpId, byte flags, uint counter)
    {
        var data = new byte[37];
        SHA256.HashData(Encoding.UTF8.GetBytes(rpId)).CopyTo(data, 0);
        data[32] = flags;
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(33), counter);
        return data;
    }

    /// <summary>WebAuthnのJSON向けBase64へ変換</summary>
    private static string Encode(byte[] bytes) => WebEncoders.Base64UrlEncode(bytes);

    /// <summary>試験で使う短いCBOR文字列を書き込む</summary>
    private static void WriteText(Stream stream, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        stream.WriteByte((byte)(0x60 + bytes.Length));
        stream.Write(bytes);
    }

    /// <summary>試験で使うCBORバイト列を書き込む</summary>
    private static void WriteBytes(Stream stream, byte[] bytes)
    {
        stream.WriteByte(0x58);
        stream.WriteByte((byte)bytes.Length);
        stream.Write(bytes);
    }

    /// <summary>試験の秘密鍵を解放</summary>
    public void Dispose() => key.Dispose();
}
