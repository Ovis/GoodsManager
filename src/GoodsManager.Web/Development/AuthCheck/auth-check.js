"use strict";

const element = id => document.getElementById(id);
const state = { connected: false, busy: false, session: null, setupRequired: false, passkeys: [] };
const passkeyAvailable = Boolean(window.PublicKeyCredential?.parseCreationOptionsFromJSON
  && window.PublicKeyCredential?.parseRequestOptionsFromJSON
  && navigator.credentials);
const history = [];

// 応答の本文・パスワード・チャレンジ・認証データを履歴へ保存しない
function record(message, error = false) {
  history.push(new Date().toLocaleTimeString() + " " + message);
  if (history.length > 16) history.shift();
  element("result").textContent = history.join("\n");
  element("result").dataset.error = String(error);
}

class ApiFailure extends Error {
  constructor(response, body) {
    super("HTTP " + response.status);
    this.status = response.status;
    this.code = typeof body?.code === "string" ? body.code : "応答のコードなし";
    this.traceId = typeof body?.traceId === "string" ? body.traceId : "なし";
    this.reason = typeof body?.reason === "string" ? body.reason : null;
    this.failureType = typeof body?.failureType === "string" ? body.failureType : null;
  }
}

// 認証状態の変化をまたいでCSRFトークンを再利用しない
async function api(path, { method = "GET", body, etag, allowAnonymous = false } = {}) {
  const headers = new Headers();
  if (method !== "GET") {
    const csrf = await api("auth/csrf");
    headers.set("X-CSRF-Token", csrf.token);
    headers.set("Content-Type", "application/json");
  }
  if (etag) headers.set("If-Match", etag);
  const response = await fetch("/api/v1/" + path, {
    method, headers, credentials: "same-origin", cache: "no-store",
    body: body === undefined ? undefined : JSON.stringify(body)
  });
  const loggedPath = path.startsWith("account/passkeys/") && path !== "account/passkeys/options" ? "account/passkeys/{id}" : path;
  record(method + " /api/v1/" + loggedPath + " → " + response.status);
  const data = response.status === 204 ? null : await response.json();
  if (!response.ok) {
    if (response.status === 401) {
      state.session = null;
      state.passkeys = [];
      element("session").textContent = "未ログイン（401）";
      element("passkeys").replaceChildren();
    }
    if (response.status === 401 && allowAnonymous) return null;
    throw new ApiFailure(response, data);
  }
  return data;
}

function updateControls() {
  element("login-fields").disabled = !state.connected || state.busy;
  element("account-fields").disabled = !state.connected || state.busy || !state.session;
  element("refresh").disabled = !state.connected || state.busy;
  element("setup").disabled = !state.setupRequired || Boolean(state.session);
  document.querySelectorAll("[data-passkey]").forEach(control => { control.disabled = !passkeyAvailable; });
}

// 処理中は新しいチャレンジで既存操作を上書きさせない
async function run(label, action) {
  if (state.busy || !state.connected) return;
  state.busy = true;
  updateControls();
  record(label + "：開始");
  try {
    await action();
    record(label + "：終了", element("result").dataset.error === "true");
  } catch (error) {
    if (error instanceof ApiFailure) {
      const hint = error.status === 412 ? "入力を確認し、状態・一覧を再取得してください。" : "自動再送は行っていません。";
      const reason = error.reason ? " / reason=" + error.reason + (error.failureType ? " (" + error.failureType + ")" : "") : "";
      record(error.message + " / " + error.code + reason + " / traceId=" + error.traceId + "\n" + hint, true);
    } else if (error?.name === "NotAllowedError" || error?.name === "AbortError") {
      record("OSの本人確認が完了しませんでした（キャンセル・期限切れ等）。やり直す場合は再度ボタンを押してください。", true);
    } else {
      record("操作を完了できませんでした。接続・ブラウザの対応状況を確認し、状態を再取得してください。", true);
    }
  } finally {
    document.querySelectorAll('input[type="password"]').forEach(input => { input.value = ""; });
    state.busy = false;
    updateControls();
  }
}

// 明示的な再取得か成功後だけETagのスナップショットを置き換える
async function refresh() {
  const setup = await api("setup/status");
  const session = await api("auth/session", { allowAnonymous: true });
  const passkeys = session ? await api("account/passkeys") : [];
  state.setupRequired = setup.setupRequired;
  state.session = session;
  state.passkeys = passkeys;
  element("status").textContent = (setup.setupRequired ? "初期作成が必要" : "初期作成済み") + " ／ 最終取得 " + new Date().toLocaleTimeString();
  element("session").textContent = session ? JSON.stringify({
    loginId: session.user.loginId, persistent: session.persistent, expiresAt: session.expiresAt,
    etag: session.user.etag
  }, null, 2) : "未ログイン（401）";
  renderPasskeys();
}

async function refreshedAfterSuccess() {
  record("API操作成功。状態を再取得します。");
  try {
    await refresh();
  } catch {
    // 保存成功を失敗と誤表示して変更の再送を誘発しない
    state.session = null;
    state.passkeys = [];
    element("session").textContent = "操作成功後の状態取得に失敗。再取得してください。";
    element("passkeys").replaceChildren();
    record("状態の再取得に失敗しました。変更を再送せず、状態・一覧を再取得してください。", true);
  }
}

async function assertion(options) {
  const credential = await navigator.credentials.get({
    publicKey: PublicKeyCredential.parseRequestOptionsFromJSON(options.publicKeyOptions)
  });
  if (!credential) throw new DOMException("認証を中止", "NotAllowedError");
  return { challengeId: options.challengeId, credential: serializePasskey(credential) };
}

async function verification(purpose) {
  if (element("verification-method").value === "password") {
    return { password: element("verification-password").value };
  }
  return assertion(await api("auth/reauthenticate", { method: "POST", body: { purpose } }));
}

function renderPasskeys() {
  const container = element("passkeys");
  container.replaceChildren();
  if (!state.passkeys.length) {
    container.textContent = state.session ? "登録済みパスキーなし" : "未ログイン";
  }
  for (const passkey of state.passkeys) {
    const row = document.createElement("div");
    row.className = "passkey";
    const label = document.createElement("label");
    label.textContent = "パスキー名";
    const name = document.createElement("input");
    name.value = passkey.displayName;
    name.autocomplete = "off";
    label.append(name);
    const metadata = document.createElement("p");
    metadata.className = "metadata";
    metadata.textContent = "作成日時：" + passkey.createdAt + "\nETag：" + passkey.etag;
    const actions = document.createElement("div");
    actions.className = "actions";
    const rename = document.createElement("button");
    rename.type = "button";
    rename.textContent = "取得済みETagで名前を変更";
    rename.addEventListener("click", () => run("パスキー名変更", async () => {
      await api("account/passkeys/" + encodeURIComponent(passkey.id), {
        method: "PUT", etag: passkey.etag, body: { displayName: name.value }
      });
      await refreshedAfterSuccess();
    }));
    const remove = document.createElement("button");
    remove.type = "button";
    remove.textContent = "本人確認して削除";
    remove.addEventListener("click", () => run("パスキー削除", async () => {
      // 本人確認で標準スタンプが更新される前に取得したETagを送る
      const proof = await verification("passkey-delete:" + passkey.id);
      await api("account/passkeys/" + encodeURIComponent(passkey.id), { method: "DELETE", etag: passkey.etag, body: proof });
      await refreshedAfterSuccess();
    }));
    actions.append(rename, remove);
    row.append(label, metadata, actions);
    container.append(row);
  }
}

element("refresh").addEventListener("click", () => run("状態取得", refresh));
element("setup").addEventListener("click", () => {
  if (!element("login-form").reportValidity()) return;
  run("初期作成", async () => {
    await api("setup", { method: "POST", body: { loginId: element("login-id").value, password: element("login-password").value } });
    await refreshedAfterSuccess();
  });
});
element("login-form").addEventListener("submit", event => {
  event.preventDefault();
  run("パスワードログイン", async () => {
    await api("auth/password", { method: "POST", body: {
      loginId: element("login-id").value, password: element("login-password").value, rememberMe: element("remember").checked
    } });
    await refreshedAfterSuccess();
  });
});
element("passkey-login").addEventListener("click", () => run("パスキーログイン", async () => {
  const options = await api("auth/passkey/options", { method: "POST", body: {
    loginId: element("login-id").value || null, rememberMe: element("remember").checked
  } });
  await api("auth/passkey/verify", { method: "POST", body: await assertion(options) });
  await refreshedAfterSuccess();
}));
for (const [id, path] of [["logout", "auth/logout"], ["revoke", "auth/revoke-all"]]) {
  element(id).addEventListener("click", () => run(element(id).textContent, async () => {
    await api(path, { method: "POST", body: {} });
    await refreshedAfterSuccess();
  }));
}
element("id-form").addEventListener("submit", event => {
  event.preventDefault();
  run("ログインID変更", async () => {
    await api("account", { method: "PUT", etag: state.session.user.etag, body: { loginId: element("new-id").value } });
    await refreshedAfterSuccess();
  });
});
element("verification-method").addEventListener("change", () => {
  element("verification-password-label").hidden = element("verification-method").value !== "password";
  element("verification-password").value = "";
});
element("add-form").addEventListener("submit", event => {
  event.preventDefault();
  run("パスキー追加", async () => {
    const proof = await verification("passkey-add");
    const options = await api("account/passkeys/options", { method: "POST", body: { verification: proof } });
    const credential = await navigator.credentials.create({
      publicKey: PublicKeyCredential.parseCreationOptionsFromJSON(options.publicKeyOptions)
    });
    if (!credential) throw new DOMException("登録を中止", "NotAllowedError");
    await api("account/passkeys", { method: "POST", body: {
      challengeId: options.challengeId, credential: serializePasskey(credential), displayName: element("passkey-name").value
    } });
    await refreshedAfterSuccess();
  });
});
element("password-form").addEventListener("submit", event => {
  event.preventDefault();
  run("パスワード変更", async () => {
    const proof = await verification("password");
    await api("account/password", { method: "POST", body: { newPassword: element("new-password").value, verification: proof } });
    await refreshedAfterSuccess();
  });
});

async function initialize() {
  try {
    const response = await fetch("/dev/auth-check/config", { cache: "no-store", credentials: "same-origin" });
    if (!response.ok) throw new Error("設定取得失敗");
    const config = await response.json();
    element("environment").textContent = "接続先：" + location.origin + " ／ 設定Origin：" + config.origin + " ／ RP ID：" + config.rpId;
    element("capability").textContent = "ブラウザのパスキーAPI：" + (passkeyAvailable ? "利用可能（実機の本人確認は未確認）" : "未対応。このブラウザを更新してください。");
    if (!window.isSecureContext || location.protocol !== "https:" || location.origin !== config.origin) {
      record("設定Originと同じHTTPS URLで開いてください：" + config.origin + "/dev/auth-check", true);
      return;
    }
    state.connected = true;
    await run("初期状態取得", refresh);
  } catch {
    record("接続条件を取得できません。開発用サーバーを確認してください。", true);
  } finally {
    updateControls();
  }
}
initialize();
