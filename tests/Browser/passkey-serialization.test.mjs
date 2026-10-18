import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { test } from "node:test";
import vm from "node:vm";

const context = { btoa, Uint8Array };
vm.runInNewContext(readFileSync(new URL("../../src/GoodsManager.Web/Development/AuthCheck/passkey-serialization.js", import.meta.url), "utf8"), context);
const wire = credential => JSON.parse(JSON.stringify(context.serializePasskey(credential)));
const buffer = (...bytes) => Uint8Array.from(bytes).buffer;

test("不完全なtoJSONを使わず、拡張結果と登録のバイト列を送る", () => {
  const credential = {
    id: "-_8A", rawId: buffer(251, 255, 0), type: "public-key", authenticatorAttachment: "cross-platform",
    getClientExtensionResults: () => ({ credProps: { rk: true } }),
    response: {
      clientDataJSON: buffer(0, 1, 255), attestationObject: buffer(251, 255),
      getTransports: () => ["internal", "hybrid"], getPublicKeyAlgorithm: () => -7,
      getPublicKey: () => buffer(1, 2), getAuthenticatorData: () => buffer(3, 4)
    },
    toJSON: () => ({ id: "-_8A", type: "public-key", response: {} })
  };
  assert.equal(JSON.parse(JSON.stringify(credential)).clientExtensionResults, undefined);
  assert.deepEqual(wire(credential), {
    id: "-_8A", rawId: "-_8A", type: "public-key", authenticatorAttachment: "cross-platform",
    clientExtensionResults: { credProps: { rk: true } },
    response: {
      clientDataJSON: "AAH_", attestationObject: "-_8", transports: ["internal", "hybrid"],
      publicKeyAlgorithm: -7, publicKey: "AQI", authenticatorData: "AwQ"
    }
  });
});

test("認証と本人確認の署名・ユーザーハンドルを同じ変換で保全する", () => {
  const credential = {
    id: "AQI", rawId: buffer(1, 2), type: "public-key", authenticatorAttachment: null,
    getClientExtensionResults: () => ({}),
    response: { clientDataJSON: buffer(5), authenticatorData: buffer(6), signature: buffer(255, 251), userHandle: buffer(8, 9) },
    toJSON() { throw new TypeError("Illegal invocation"); }
  };
  assert.deepEqual(wire(credential), {
    id: "AQI", rawId: "AQI", type: "public-key", authenticatorAttachment: null, clientExtensionResults: {},
    response: { clientDataJSON: "BQ", authenticatorData: "Bg", signature: "__s", userHandle: "CAk" }
  });
  credential.response.userHandle = null;
  assert.equal(wire(credential).response.userHandle, null);
});
