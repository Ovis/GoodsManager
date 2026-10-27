import { serializePasskey } from './passkey-serialization.mjs';

/** ブラウザの対応状況だけを調べ、認証器やOSへの要求は操作ボタンから行う。 */
export function passkeyCapabilities() {
  return {
    assertion: !!(window.isSecureContext && window.PublicKeyCredential?.parseRequestOptionsFromJSON),
    creation: !!(window.isSecureContext && window.PublicKeyCredential?.parseCreationOptionsFromJSON)
  };
}

export async function assertPasskey(options:PublicKeyCredentialRequestOptionsJSON,signal?:AbortSignal) {
  if(!passkeyCapabilities().assertion)throw new DOMException('Unsupported','NotSupportedError');
  const credential=await navigator.credentials.get({publicKey:PublicKeyCredential.parseRequestOptionsFromJSON(options),signal}) as PublicKeyCredential|null;
  if(!credential)throw new DOMException('Cancelled','NotAllowedError');
  return serializePasskey(credential);
}

export async function createPasskey(options:PublicKeyCredentialCreationOptionsJSON,signal?:AbortSignal) {
  if(!passkeyCapabilities().creation)throw new DOMException('Unsupported','NotSupportedError');
  const credential=await navigator.credentials.create({publicKey:PublicKeyCredential.parseCreationOptionsFromJSON(options),signal}) as PublicKeyCredential|null;
  if(!credential)throw new DOMException('Cancelled','NotAllowedError');
  return serializePasskey(credential);
}
