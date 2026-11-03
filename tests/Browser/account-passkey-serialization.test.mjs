import test from 'node:test';
import assert from 'node:assert/strict';
import { serializePasskey } from '../../src/GoodsManager.Client/src/passkey-serialization.mjs';

const buffer = (...bytes) => Uint8Array.from(bytes).buffer;

const credential = (response, extensions = {}) => ({
    id:'credential',
    rawId:buffer(255,254,253),
    type:'public-key',
    authenticatorAttachment:'platform',
    getClientExtensionResults:()=>extensions,
    toJSON(){
        throw new TypeError('Illegal invocation');
    },
    response
});

test('パスキー登録ではtoJSONに依存せず、拡張結果と認証器の応答を渡す', () => {
    for (const extensions of [{}, {credProps:{rk:true}}]) {
        const result = JSON.parse(JSON.stringify(serializePasskey(credential({
            clientDataJSON:buffer(1,2,3),attestationObject:buffer(255,255),
            getAuthenticatorData:()=>buffer(4,5),getPublicKey:()=>buffer(6),getPublicKeyAlgorithm:()=>-7,getTransports:()=>['internal']
        }, extensions))));

        assert.deepEqual(result.clientExtensionResults, extensions);
        assert.equal(result.rawId, '__79');
        assert.deepEqual(result.response, {clientDataJSON:'AQID',attestationObject:'__8',authenticatorData:'BAU',publicKey:'Bg',publicKeyAlgorithm:-7,transports:['internal']});
    }
});

test('パスキー認証の署名とユーザーハンドルを保存し、大きなバイト列にも対応する', () => {
    const signature = new Uint8Array(200000).fill(255).buffer;

    for (const userHandle of [null, buffer(8,9)]) {
        const result = JSON.parse(JSON.stringify(serializePasskey(credential({clientDataJSON:buffer(1),authenticatorData:buffer(2),signature,userHandle}))));

        assert.equal(result.response.userHandle, userHandle === null ? null : 'CAk');
        assert.equal(result.response.authenticatorData, 'Ag');
        assert.deepEqual(Buffer.from(result.response.signature,'base64url'), Buffer.from(signature));
        assert.equal(Object.hasOwn(result.response,'attestationObject'), false);
    }
});
