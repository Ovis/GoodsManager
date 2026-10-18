import { test } from "node:test";
import assert from "node:assert/strict";
import { MasterRequests, ApiFailure } from "../../src/GoodsManager.Web/Development/MasterCheck/requests.mjs";

function harness() {
  const sent = [];
  let now = 1000;
  let fail = null;
  let token = 0;
  let response = { id: "saved", etag: '"old"' };
  const api = new MasterRequests({
    key: () => "fixed-key", now: () => now,
    fetcher: async (path, options) => {
      sent.push({ path, ...options });
      if (path.endsWith("auth/csrf")) {
        if (fail === "csrf") throw new TypeError("network");
        return Response.json({ token: "csrf-" + ++token });
      }
      if (fail === "network") throw new TypeError("network");
      if (fail === "invalid-json") return new Response("bad JSON", { status: 200 });
      if (typeof fail === "number") return Response.json({ code: "TEST_FAILURE" }, { status: fail });
      return Response.json(response);
    }
  });
  return { api, sent, time: value => { now = value; }, fail: value => { fail = value; }, response: value => { response = value; } };
}

test("再送は元の本文・キー・ETag・期限を保持し、CSRFだけを取得し直す", async () => {
  const h = harness();
  const input = { name: "original" };
  await h.api.write("masters/works/id", "PUT", input, '"old"');
  const original = h.api.last;
  input.name = "edited locally";
  h.time(2000);
  await h.api.replay();
  const writes = h.sent.filter(x => x.method !== "GET");
  assert.equal(writes.length, 2);
  assert.equal(writes[0].body, '{"name":"original"}');
  assert.equal(writes[1].body, writes[0].body);
  assert.equal(writes[1].headers["Idempotency-Key"], writes[0].headers["Idempotency-Key"]);
  assert.equal(writes[1].headers["If-Match"], '"old"');
  assert.notEqual(writes[0].headers["X-CSRF-Token"], writes[1].headers["X-CSRF-Token"]);
  assert.equal(h.api.last, original);
  assert.equal(original.sentAt, 1000);
});

test("通信失敗は自動再送せず、新しいキーでの変更を停止する", async () => {
  const h = harness();
  h.fail("network");
  await assert.rejects(h.api.write("masters/works", "POST", { name: "a" }));
  assert.equal(h.api.status, "unknown");
  assert.equal(h.sent.length, 2);
  assert.throws(() => h.api.write("masters/works", "POST", { name: "b" }));
  h.fail(null);
  await h.api.replay();
  assert.equal(h.api.status, "success");
});

test("結果不明の再送でCSRF取得や認証に失敗しても、元要求を失敗扱いにしない", async () => {
  for (const failure of ["csrf", 401, 403, 503]) {
    const h = harness();
    h.fail("network");
    await assert.rejects(h.api.write("masters/works", "POST", { name: "a" }));
    const saved = h.api.last;
    h.fail(failure);
    await assert.rejects(h.api.replay());
    assert.equal(h.api.status, "unknown");
    assert.equal(h.api.last, saved);
  }
});

test("412では要求を保持し、自動再取得と自動再送をしない", async () => {
  const h = harness();
  h.fail(412);
  await assert.rejects(h.api.write("masters/works/id/relations", "POST", { addIds: ["id"], removeIds: [] }, '"stale"'), ApiFailure);
  assert.equal(h.api.status, "failed");
  assert.equal(h.api.last.etag, '"stale"');
  assert.equal(h.sent.length, 2);
});

test("成功HTTPの本文が読めない場合も結果不明として送信情報を保持する", async () => {
  const h = harness();
  h.fail("invalid-json");
  await assert.rejects(h.api.write("masters/works", "POST", { name: "a" }));
  assert.equal(h.api.status, "unknown");
});

test("初回送信から7日経過した要求は送らず、再送で期限を延長しない", async () => {
  const h = harness();
  await h.api.write("masters/works", "POST", { name: "a" });
  h.time(1000 + 6 * 86400000);
  await h.api.replay();
  h.time(1000 + 7 * 86400000);
  const count = h.sent.length;
  assert.throws(() => h.api.replay(), /7日/);
  assert.equal(h.sent.length, count);
});

test("再取得で保存済み要求を置き換えず、同時送信を拒否する", async () => {
  const h = harness();
  const pending = h.api.write("masters/works", "POST", { name: "a" });
  assert.throws(() => h.api.write("masters/works", "POST", { name: "b" }));
  await pending;
  const saved = h.api.last;
  h.response({ etag: '"new"' });
  await h.api.read("masters/works/id");
  assert.equal(h.api.last, saved);
});
