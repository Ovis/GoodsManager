/** 署名対象のバイト列を変えず、パディングなしのBase64urlへ変換する。 */
function bytes(buffer) {
  if (buffer === undefined || buffer === null) return buffer;
  let binary = '';
  for (const byte of new Uint8Array(buffer)) binary += String.fromCharCode(byte);
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

/** 登録・認証のどちらも、空の拡張結果と認証器が返した情報をAPIへ渡す。 */
export function serializePasskey(credential) {
  const response = credential.response;
  return {
    id: credential.id,
    rawId: bytes(credential.rawId),
    type: credential.type,
    authenticatorAttachment: credential.authenticatorAttachment,
    clientExtensionResults: credential.getClientExtensionResults(),
    response: {
      clientDataJSON: bytes(response.clientDataJSON),
      attestationObject: bytes(response.attestationObject),
      authenticatorData: bytes(response.authenticatorData ?? response.getAuthenticatorData?.()),
      publicKey: bytes(response.getPublicKey?.()),
      publicKeyAlgorithm: response.getPublicKeyAlgorithm?.(),
      transports: response.getTransports?.(),
      signature: bytes(response.signature),
      userHandle: bytes(response.userHandle)
    }
  };
}
