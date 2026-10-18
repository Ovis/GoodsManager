export class ApiFailure extends Error {
  constructor(status, body) {
    super("HTTP " + status);
    this.status = status;
    this.code = body?.code ?? "応答のコードなし";
    this.traceId = body?.traceId ?? "なし";
    this.errors = body?.errors;
  }
}

// 送信情報はこのページのメモリだけに保持。入力欄や再取得したETagから再構築しない。
export class MasterRequests {
  constructor({ fetcher = (url, options) => fetch(url, options), now = () => Date.now(), key = () => crypto.randomUUID(), record = () => {} } = {}) {
    Object.assign(this, { fetcher, now, key, record });
    this.last = null;
    this.status = "none";
    this.inFlight = false;
  }

  async read(path) { return this.transport(path, { method: "GET" }); }

  async transport(path, options) {
    const response = await this.fetcher("/api/v1/" + path, { ...options, credentials: "same-origin", cache: "no-store" });
    this.record(options.method + " /api/v1/" + path + " → " + response.status);
    let body;
    try { body = await response.json(); }
    catch (error) { if (response.ok) throw error; }
    if (!response.ok) throw new ApiFailure(response.status, body);
    return body;
  }

  write(path, method, body, etag) {
    if (this.inFlight || this.status === "unknown") throw new Error("結果不明の要求を先に確認してください。");
    this.last = Object.freeze({ path, method, body: JSON.stringify(body), etag: etag ?? null, key: this.key(), sentAt: this.now() });
    return this.send();
  }

  replay() {
    if (!this.last || this.inFlight) throw new Error("再送できる要求がありません。");
    if (this.now() - this.last.sentAt >= 7 * 86400000) throw new Error("最初の送信から7日経過しています。再送せず最新状態を確認してください。");
    return this.send();
  }

  async send() {
    const saved = this.last;
    const wasUnknown = this.status === "unknown";
    this.inFlight = true;
    let attempted = false;
    try {
      const csrf = await this.read("auth/csrf");
      const headers = { "Content-Type": "application/json", "X-CSRF-Token": csrf.token, "Idempotency-Key": saved.key };
      if (saved.etag) headers["If-Match"] = saved.etag;
      attempted = true;
      const body = await this.transport(saved.path, { method: saved.method, headers, body: saved.body });
      this.status = "success";
      return body;
    } catch (error) {
      this.status = wasUnknown || !(error instanceof ApiFailure || !attempted) ? "unknown" : "failed";
      throw error;
    } finally { this.inFlight = false; }
  }
}
