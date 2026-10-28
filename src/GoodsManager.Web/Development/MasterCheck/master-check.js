import { ApiFailure, MasterRequests } from "/dev/master-check/requests.mjs";

const el = id => document.getElementById(id);
const state = { session: null, busy: false, selected: null, list: null, query: null, relations: null };
const history = [];
function record(message, error = false) {
  history.push(new Date().toLocaleTimeString() + " " + message);
  if (history.length > 30) history.shift();
  el("result").textContent = history.join("\n");
  el("result").dataset.error = String(error);
}
const api = new MasterRequests({ record });
const kind = () => el("kind").value;
const relatedKind = () => kind() === "works" ? "series" : "works";
const supportsRelations = () => ["works", "series", "characters"].includes(kind());
const ids = value => value.split(/[\s,]+/u).filter(Boolean);
const path = () => "masters/" + kind();

function controls() {
  el("controls").disabled = state.busy || !state.session;
  el("session-refresh").disabled = state.busy;
  el("create").disabled = Boolean(state.selected) || api.status === "unknown";
  el("save").disabled = !state.selected || api.status === "unknown";
  el("selected-refresh").disabled = !state.selected;
  el("relations-refresh").disabled = !state.selected;
  el("delta").disabled = !state.relations || api.status === "unknown";
  el("previous").disabled = !state.list || state.list.page <= 1;
  el("next").disabled = !state.list || state.list.page >= state.list.totalPages;
  el("relation-previous").disabled = !state.relations || state.relations.page <= 1;
  el("relation-next").disabled = !state.relations || state.relations.page >= state.relations.totalPages;
  el("replay").disabled = state.busy || !state.session || !api.last;
  el("organization-fields").hidden = kind() !== "organizations";
  el("category-fields").hidden = kind() !== "categories";
  el("sort-order").disabled = kind() !== "categories";
  el("priority-label").hidden = kind() !== "characters";
  el("relation-section").hidden = !supportsRelations();
  el("relation-kind").textContent = kind() === "works" ? "指定するID：シリーズID" : "指定するID：作品ID";
  el("request").textContent = api.last ? JSON.stringify({ ...api.last, status: api.status }, null, 2) : "変更要求なし";
}

async function run(label, action, mutation = false) {
  if (state.busy) return;
  state.busy = true;
  controls();
  if (mutation) el("response").textContent = "変更要求を準備しています…";
  record(label + "：開始");
  try {
    await action();
    record(label + "：終了");
  } catch (error) {
    if (error instanceof ApiFailure) {
      if (mutation) el("response").textContent = JSON.stringify({ status: error.status, code: error.code, traceId: error.traceId, errors: error.errors }, null, 2);
      if (error.status === 401) {
        state.session = null;
        el("session").textContent = "未ログイン（401）。認証確認画面でログイン後、ログイン状態を取得してください。";
      }
      record(error.message + " / " + error.code + " / traceId=" + error.traceId
        + (error.errors ? "\n" + JSON.stringify(error.errors) : "")
        + "\n入力を保持しています。自動再送・自動再取得は行っていません。", true);
    } else {
      if (mutation) el("response").textContent = api.status === "unknown" ? "結果不明。送信情報を保持しています。" : "変更を完了できませんでした。操作ログを確認してください。";
      record(api.status === "unknown" ? "保存結果が不明です。送信情報と入力を保持しています。同じ要求の再送で結果を確認してください。"
        : "操作を完了できませんでした。" + (error?.message ?? "接続を確認してください。"), true);
    }
  } finally { state.busy = false; controls(); }
}

async function session() {
  state.session = await api.connect();
  el("session").textContent = JSON.stringify({ loginId: state.session.user.loginId, persistent: state.session.persistent, expiresAt: state.session.expiresAt }, null, 2);
}

function clearSelection() {
  state.selected = null;
  state.relations = null;
  el("snapshot").textContent = "新規入力";
  el("relation-status").textContent = "未取得";
  el("relations").replaceChildren();
  for (const id of ["name", "website-url", "memo", "parent-id", "add-ids", "remove-ids"]) el(id).value = "";
  el("sort-order").value = "0";
}

function snapshot(item, fill = true) {
  state.selected = item;
  el("snapshot").textContent = JSON.stringify(item, null, 2);
  state.relations = null;
  el("relations").replaceChildren();
  el("relation-status").textContent = "関連一覧は未取得です。";
  if (fill) {
    el("name").value = item.name;
    el("website-url").value = item.websiteUrl ?? "";
    el("memo").value = item.memo ?? "";
    el("parent-id").value = item.parentId ?? "";
    el("sort-order").value = item.sortOrder ?? 0;
  }
}

async function select(id) { snapshot(await api.read(path() + "/" + encodeURIComponent(id))); }

function row(item, choose) {
  const box = document.createElement("div");
  box.className = "row";
  const title = document.createElement("strong");
  title.textContent = item.name;
  const meta = document.createElement("p");
  meta.className = "metadata";
  meta.textContent = "ID：" + item.id + " ／ revision：" + item.revision;
  box.append(title, meta);
  if (choose) {
    const button = document.createElement("button");
    button.type = "button";
    button.textContent = "この対象を取得して編集";
    button.addEventListener("click", () => run("選択対象取得", () => select(item.id)));
    box.append(button);
  }
  return box;
}

async function list(page, apply = false) {
  const query = apply ? { q: el("query").value, size: el("page-size").value, priority: kind() === "characters" ? ids(el("priority").value) : [] } : state.query;
  const search = new URLSearchParams({ q: query.q, page: String(page), pageSize: query.size });
  for (const id of query.priority) search.append("relatedWorkIds", id);
  const result = await api.read(path() + "?" + search);
  state.query = query;
  state.list = result;
  el("list-status").textContent = result.totalCount + "件 ／ " + result.page + " / " + result.totalPages + "ページ ／ dataRevision：" + result.dataRevision + " ／ 適用した検索語：" + query.q;
  el("list").replaceChildren(...result.items.map(item => row(item, true)));
}

function input() {
  const body = { name: el("name").value };
  if (kind() === "organizations") Object.assign(body, { websiteUrl: el("website-url").value || null, memo: el("memo").value || null });
  if (kind() === "categories") Object.assign(body, { parentId: el("parent-id").value || null, sortOrder: Number(el("sort-order").value) });
  return body;
}

async function save(create) {
  if (!el("edit-form").reportValidity()) return;
  const selected = state.selected;
  const data = await api.write(path() + (create ? "" : "/" + selected.id), create ? "POST" : "PUT", input(), create ? null : selected.etag);
  el("response").textContent = JSON.stringify(data, null, 2);
  snapshot(data, false);
  record("保存成功。一覧・カテゴリツリーは必要に応じて再取得してください。");
}

async function relations(page = 1) {
  const result = await api.read(path() + "/" + state.selected.id + "/relations?page=" + page + "&pageSize=50");
  state.relations = result;
  el("relation-status").textContent = JSON.stringify({ totalCount: result.totalCount, page: result.page, totalPages: result.totalPages, masterEtag: result.masterEtag, dataRevision: result.dataRevision }, null, 2);
  el("relations").replaceChildren(...result.items.map(item => row(item, false)));
}

async function tree() {
  const data = await api.read("categories/tree");
  const nodes = [...data.nodes].sort((a, b) => a.sortOrder - b.sortOrder || a.id.localeCompare(b.id));
  const roots = nodes.filter(x => x.parentId === null);
  el("tree-status").textContent = "全" + nodes.length + "件 ／ ルート" + roots.length + "件";
  const container = el("tree");
  container.replaceChildren();
  const groups = new Map();
  for (const node of nodes) {
    if (!groups.has(node.parentId)) groups.set(node.parentId, []);
    groups.get(node.parentId).push(node);
  }
  const stack = [{ parent: null, target: container }];
  const visited = new Set();
  while (stack.length) {
    const { parent, target } = stack.pop();
    const ul = document.createElement("ul");
    for (const node of groups.get(parent) ?? []) {
      if (visited.has(node.id)) continue;
      visited.add(node.id);
      const li = document.createElement("li");
      li.textContent = node.name + " ／ ID：" + node.id;
      ul.append(li);
      stack.push({ parent: node.id, target: li });
    }
    if (ul.childNodes.length) target.append(ul);
  }
  if (visited.size !== nodes.length) record("ツリーに到達できないカテゴリがあります。表示とAPI応答を確認してください。", true);
}

el("session-refresh").addEventListener("click", () => run("ログイン状態取得", session));
el("search-form").addEventListener("submit", event => { event.preventDefault(); run("一覧取得", () => list(1, true)); });
el("previous").addEventListener("click", () => run("一覧取得", () => list(state.list.page - 1)));
el("next").addEventListener("click", () => run("一覧取得", () => list(state.list.page + 1)));
el("kind").addEventListener("change", () => {
  clearSelection(); state.list = null; state.query = null;
  el("list").replaceChildren(); el("list-status").textContent = "未取得"; controls();
});
el("new").addEventListener("click", () => { clearSelection(); controls(); });
el("selected-refresh").addEventListener("click", () => run("選択対象再取得", () => select(state.selected.id)));
el("edit-form").addEventListener("submit", event => { event.preventDefault(); run("新規作成", () => save(true), true); });
el("save").addEventListener("click", () => run("対象更新", () => save(false), true));
el("relations-refresh").addEventListener("click", () => run("関連一覧取得", () => relations()));
el("relation-previous").addEventListener("click", () => run("関連一覧取得", () => relations(state.relations.page - 1)));
el("relation-next").addEventListener("click", () => run("関連一覧取得", () => relations(state.relations.page + 1)));
el("relation-form").addEventListener("submit", event => {
  event.preventDefault();
  run("関連差分保存", async () => {
    const data = await api.write(path() + "/" + state.selected.id + "/relations", "POST", { addIds: ids(el("add-ids").value), removeIds: ids(el("remove-ids").value) }, state.relations.masterEtag);
    el("response").textContent = JSON.stringify(data, null, 2);
    record("保存成功。入力と取得済みETagを保持しています。次の編集前に対象・関連を再取得してください。");
  }, true);
});
el("tree-refresh").addEventListener("click", () => run("カテゴリツリー取得", tree));
el("replay").addEventListener("click", () => run("同一要求再送", async () => {
  const result = await api.replay();
  el("response").textContent = JSON.stringify(result, null, 2);
  record("保存済み要求の結果を確認しました。表示中の入力・対象・ETagは置き換えていません。");
}, true));
window.addEventListener("beforeunload", event => {
  if (api.status === "unknown" || api.inFlight) { event.preventDefault(); event.returnValue = ""; }
});
if (window.isSecureContext && location.protocol === "https:") run("初期ログイン状態取得", session);
else { record("https://localhost:7443/dev/master-check で開いてください。", true); controls(); }
