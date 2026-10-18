import { MasterRequests, ApiFailure } from "/dev/master-check/requests.mjs";
const $ = id => document.getElementById(id);
const show = (id, value) => { $(id).textContent = typeof value === "string" ? value : JSON.stringify(value, null, 2); };
const log = [];
let loggedIn = false, busy = false, selected = null;
const api = new MasterRequests({ record: line => {
  log.push(new Date().toLocaleTimeString() + " " + line);
  show("result", log.slice(-30).join("\n"));
} });
const fields = { condition: "unopened", storageLocation: "場所A", purchasePlace: "購入先A", memo: "元ロットメモ" };
$("fixture").value = JSON.stringify({ product: {
  name: "P05ブラウザ確認", categoryId: "b390a000-7024-4c01-a000-000000000100",
  version: null, manufacturerId: null, sellerId: null, memo: null,
  workIds: [], characterIds: [], tagIds: [], identifiers: [], links: []
}, initialLot: { quantity: 3, fields } }, null, 2);

function render() {
  $("controls").disabled = busy || !loggedIn;
  $("save").disabled = !selected || busy || !loggedIn || api.status === "unknown";
  $("create").disabled = busy || !loggedIn || api.status === "unknown";
  $("replay").disabled = busy || !loggedIn || !api.last;
  $("session-refresh").disabled = busy;
  show("request", api.last ? { ...api.last, status: api.status } : "変更要求なし");
}
async function run(action, mutation = false) {
  if (busy) return;
  busy = true; render();
  try {
    if (mutation) show("response", "送信中…");
    await action();
  } catch (error) {
    if (error instanceof ApiFailure) {
      log.push("HTTP " + error.status + " / " + error.code + " / traceId=" + error.traceId);
      show("response", { status: error.status, code: error.code, errors: error.errors, traceId: error.traceId });
    } else { log.push(error.message); if (mutation) show("response", error.message); }
    log.push("入力を保持しています。自動再送・自動再取得は行っていません。");
    show("result", log.slice(-30).join("\n"));
  } finally { busy = false; render(); }
}
function adopt(value, id) {
  selected = { id, etag: value.etag };
  $("product-id").value = id;
  show("snapshot", value);
}
async function session() {
  loggedIn = false; selected = null;
  try { const value = await api.read("auth/session"); loggedIn = true; show("session", value); }
  catch (error) { show("session", error instanceof ApiFailure ? { status: error.status, code: error.code } : error.message); }
}
$("session-refresh").addEventListener("click", () => run(session));
$("create").addEventListener("click", () => run(async () => {
  const input = JSON.parse($("fixture").value);
  const value = await api.write("dev/products", "POST", input);
  show("response", value); adopt(value, value.id);
}, true));
$("refresh").addEventListener("click", () => run(async () => {
  const id = $("product-id").value.trim();
  if (!/^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/.test(id)) throw new Error("商品IDを確認してください。");
  const value = await api.read("products/" + id);
  adopt(value, id);
}));
$("save").addEventListener("click", () => run(async () => {
  const input = JSON.parse($("commands").value);
  const value = await api.write("products/" + selected.id + "/inventory-commands", "POST", input, selected.etag);
  show("response", value); adopt(value, selected.id);
}, true));
$("replay").addEventListener("click", () => run(async () => {
  const value = await api.replay(); show("response", value);
  log.push("保存済み要求の結果を確認しました。編集用ETagは置き換えていません。"); show("result", log.slice(-30).join("\n"));
}, true));
window.addEventListener("beforeunload", event => {
  if (api.inFlight || api.status === "unknown") { event.preventDefault(); event.returnValue = ""; }
});
await run(session);
