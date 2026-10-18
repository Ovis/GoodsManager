"use strict";

// プロバイダーのtoJSONに依存せず、標準WebAuthnの項目とバイト列を送信
function serializePasskey(credential) {
  const response = credential.response;
  return {
    id: credential.id,
    rawId: passkeyBase64Url(credential.rawId),
    type: credential.type,
    authenticatorAttachment: credential.authenticatorAttachment,
    clientExtensionResults: credential.getClientExtensionResults(),
    response: {
      attestationObject: passkeyBase64Url(response.attestationObject),
      clientDataJSON: passkeyBase64Url(response.clientDataJSON),
      authenticatorData: passkeyBase64Url(response.authenticatorData ?? response.getAuthenticatorData?.()),
      publicKey: passkeyBase64Url(response.getPublicKey?.()),
      publicKeyAlgorithm: response.getPublicKeyAlgorithm?.(),
      transports: response.getTransports?.(),
      signature: passkeyBase64Url(response.signature),
      userHandle: passkeyBase64Url(response.userHandle)
    }
  };
}

// 署名対象のバイト列を変えず、パディングなしのBase64urlへ変換
function passkeyBase64Url(buffer) {
  if (buffer === undefined || buffer === null) return buffer;
  let binary = "";
  for (const byte of new Uint8Array(buffer)) binary += String.fromCharCode(byte);
  return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}
