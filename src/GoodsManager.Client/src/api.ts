import { assertPasskey, passkeyCapabilities } from './passkey-browser';
export class ApiError extends Error {
  constructor(public status: number, public code: string, public traceId?: string, public details?: unknown, public errors?: Record<string,string[]>) { super(status === 401 ? 'ログインの有効期限が切れました。ログインし直してください。' : status === 400 ? '入力またはURLの検索条件を確認してください。' : status === 429 ? '試行回数が上限に達しました。しばらく待ってからやり直してください。' : `取得・処理に失敗しました（${status} / ${code}）`); }
}
export async function api<T>(path: string, options: RequestInit = {}): Promise<T> {
  const headers = new Headers(options.headers); headers.set('Accept','application/json');
  if (options.method && options.method !== 'GET') {
    const csrf = await api<{token:string}>('/auth/csrf'); headers.set('X-CSRF-Token',csrf.token); headers.set('Content-Type','application/json');
  }
  const response = await fetch('/api/v1'+path,{...options,headers,credentials:'same-origin',cache:'no-store'});
  if(!response.ok) { const body = await response.json().catch(()=>({})); const error = new ApiError(response.status,body.code || 'HTTP_ERROR',body.traceId,body.details,body.errors); if(response.status===401 && (path==='/auth/csrf'||!path.startsWith('/auth/')))window.dispatchEvent(new Event('app:session-expired')); throw error; }
  return response.status===204 ? undefined as T : response.json();
}
export type ApiClient=typeof api&{collectionId?:string;accountId?:string};
/** クライアントの生成時に要求先を固定する。タブや後続の切替で送信先を変えない。 */
export function apiForCollection(collectionId:string,accountId?:string):ApiClient{
  return Object.assign(<T>(path:string,options:RequestInit={})=>{const headers=new Headers(options.headers);headers.set('X-Collection-Id',collectionId);if(accountId)headers.set('X-Account-Id',accountId);const url=new URL(path,'https://local.invalid');url.searchParams.delete('collectionId');return api<T>(url.pathname+url.search,{...options,headers});},{collectionId,accountId});
}
export function apiForAccount(accountId:string):ApiClient{return Object.assign(<T>(path:string,options:RequestInit={})=>{const headers=new Headers(options.headers);headers.set('X-Account-Id',accountId);const url=new URL(path,'https://local.invalid');url.searchParams.delete('collectionId');return api<T>(url.pathname+url.search,{...options,headers});},{accountId});}
export function errorText(error: unknown) { return error instanceof ApiError ? error.message : error instanceof DOMException && error.name==='NotAllowedError' ? 'パスキー認証を完了できませんでした。再試行するか、パスワードでログインしてください。' : error instanceof DOMException && error.name==='NotSupportedError' ? 'このブラウザではパスキーを利用できません。対応ブラウザを使うか、パスワードでログインしてください。' : '通信を完了できませんでした。接続を確認し、再試行してください。'; }
export async function passkeyLogin(loginId:string,rememberMe:boolean) {
  if(!passkeyCapabilities().assertion)throw new DOMException('Unsupported','NotSupportedError');
  const options=await api<{challengeId:string;publicKeyOptions:PublicKeyCredentialRequestOptionsJSON}>('/auth/passkey/options',{method:'POST',body:JSON.stringify({loginId:loginId||null,rememberMe})});
  const credential=await assertPasskey(options.publicKeyOptions);
  return api('/auth/passkey/verify',{method:'POST',body:JSON.stringify({challengeId:options.challengeId,credential})});
}
