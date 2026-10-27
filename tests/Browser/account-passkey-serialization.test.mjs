import test from 'node:test';
import assert from 'node:assert/strict';
import { serializePasskey } from '../../src/GoodsManager.Client/src/passkey-serialization.mjs';

const buffer = (...bytes) => Uint8Array.from(bytes).buffer;
const credential = response => ({id:'credential',rawId:buffer(255,254,253),type:'public-key',authenticatorAttachment:'platform',getClientExtensionResults:()=>({}),response});

test('パスキー登録では空の拡張結果を残し、認証器の応答をBase64urlで渡す', () => {
  const result = JSON.parse(JSON.stringify(serializePasskey(credential({
    clientDataJSON:buffer(1,2,3),attestationObject:buffer(255,255),
    getAuthenticatorData:()=>buffer(4,5),getPublicKey:()=>buffer(6),getPublicKeyAlgorithm:()=>-7,getTransports:()=>['internal']
  }))));
  assert.deepEqual(result.clientExtensionResults, {});
  assert.equal(result.rawId, '__79');
  assert.deepEqual(result.response, {clientDataJSON:'AQID',attestationObject:'__8',authenticatorData:'BAU',publicKey:'Bg',publicKeyAlgorithm:-7,transports:['internal']});
});

test('パスキー認証の署名とnullのユーザーハンドルを保存し、大きなバイト列にも対応する', () => {
  const signature = new Uint8Array(200000).fill(255).buffer;
  const result = JSON.parse(JSON.stringify(serializePasskey(credential({clientDataJSON:buffer(1),authenticatorData:buffer(2),signature,userHandle:null}))));
  assert.equal(result.response.userHandle, null);
  assert.equal(result.response.authenticatorData, 'Ag');
  assert.deepEqual(Buffer.from(result.response.signature,'base64url'), Buffer.from(signature));
  assert.equal(Object.hasOwn(result.response,'attestationObject'), false);
});
