import { validText } from './registration-state.mjs';

/** @typedef {{id:string,displayName:string,createdAt:string,etag:string}} Passkey */
/** @typedef {{kind:'rename'|'delete',original:Passkey,name:string,ack:boolean,conflict:boolean,latest:Passkey|null}} Action */

export function passwordError(password, confirmation) {
  if (!validText(password,128) || [...password].length<12) return '新しいパスワードは12～128文字で入力してください。NULや不正な文字は使えません。';
  return password===confirmation ? '' : '確認用パスワードが一致しません。';
}
export function passkeyNameError(name) {
  return name.length>0 && validText(name,100) ? '' : 'パスキーの名前は1～100文字で入力してください。';
}
export function accountError(error) {
  const messages={REAUTHENTICATION_REQUIRED:'本人確認に失敗しました。現在のパスワードを確認するか、登録済みパスキーでやり直してください。',REVISION_MISMATCH:'ほかの操作で登録内容が変更されました。最新一覧を取得し、内容を見直してください。',PASSKEY_CHALLENGE_INVALID:'本人確認または登録の有効期限が切れました。操作を最初からやり直してください。',PASSKEY_INVALID:'パスキーを確認できませんでした。公開URLと端末の登録先を確認し、やり直してください。',PASSKEY_EXISTS:'このパスキーはすでに登録されています。最新一覧を確認してください。',VALIDATION_FAILED:'入力の文字数・使用できる文字を確認してください。',CSRF_FAILED:'操作の有効期限が切れました。最新一覧を取得してからやり直してください。'};
  let text=messages[error?.code] || (error?.status===401?'ログインの有効期限が切れました。ログインし直してください。':error?.status===404?'対象のパスキーは見つかりません。最新一覧を確認してください。':error?.name==='NotAllowedError'||error?.name==='AbortError'?'ブラウザのパスキー操作を完了できませんでした。再試行するか、パスワードで本人確認してください。':error?.name==='NotSupportedError'?'このブラウザではパスキーを利用できません。対応ブラウザで開いてください。':'通信・処理を完了できませんでした。接続を確認してください。');
  if(error?.traceId)text+=' / 照会ID: '+error.traceId;
  return text;
}

/** 秘密や送信済みの認証情報を保持せず、競合・結果不明の要求を自動再送しない。 */
export class AccountState {
  /** @type {Passkey[]} */ passkeys=[];
  /** @type {Action|null} */ action=null;
  loaded=false;loading=false;busy=false;needsRefresh=false;uncertain='';error='';notice='';version=0;generation=0;
  listeners=new Set();controller=null;
  constructor(request,browser,onRequireLogin){this.request=request;this.browser=browser;this.onRequireLogin=onRequireLogin;}
  subscribe=(listener)=>{this.listeners.add(listener);return()=>this.listeners.delete(listener);};
  snapshot=()=>this.version;
  notify=()=>{++this.version;for(const listener of this.listeners)listener();};
  get locked(){return this.busy||this.loading||!this.loaded||this.needsRefresh;}
  cancelCeremony(){this.controller?.abort();}

  async load({keepNotice=false}={}){
    if(this.busy)return false;
    const generation=++this.generation;this.loading=true;this.error='';if(!keepNotice)this.notice='';this.notify();
    try{
      const entries=await this.request('/account/passkeys');
      if(generation!==this.generation)return false;
      this.passkeys=entries;this.loaded=true;this.needsRefresh=false;this.uncertain='';
      if(this.action?.conflict)this.action.latest=entries.find(entry=>entry.id===this.action.original.id)||null;
      return true;
    }catch(error){if(generation===this.generation){this.error=accountError(error);this.needsRefresh=true;}return false;}
    finally{if(generation===this.generation){this.loading=false;this.notify();}}
  }
  open(kind,entry){
    if(this.locked||this.action)return;
    this.action={kind,original:{...entry},name:entry.displayName,ack:false,conflict:false,latest:null};this.error='';this.notice='';this.notify();
  }
  close(){if(!this.busy){this.action=null;this.error='';this.notify();}}
  review(){
    if(this.locked||!this.action?.conflict||!this.action.latest)return;
    this.action.original={...this.action.latest};this.action.latest=null;this.action.conflict=false;this.action.ack=false;this.error='';this.notify();
  }
  async verify(method,password,purpose){
    if(method==='password'){
      if(!password || !validText(password,128))throw {code:'REAUTHENTICATION_REQUIRED'};
      return {password};
    }
    const options=await this.request('/auth/reauthenticate',{method:'POST',body:JSON.stringify({purpose})});
    return {challengeId:options.challengeId,credential:await this.browser.assertion(options.publicKeyOptions,this.controller.signal)};
  }
  async execute(kind,work,success){
    if(this.locked)return false;
    this.busy=true;this.error='';this.notice='';this.controller=new AbortController();this.notify();
    let submitted=false,succeeded=false;
    try{
      await work(()=>{submitted=true;});succeeded=true;this.action=null;
      if(kind==='password'){
        this.passkeys=[];this.loaded=false;
        this.onRequireLogin('パスワードを変更しました。すべての端末のログインを解除しました。新しいパスワードでログインしてください。');
      }else{this.notice=success;this.needsRefresh=true;}
    }catch(error){
      this.error=accountError(error);
      const unknown=submitted&&(!error?.status||error.status>=500);
      if(error?.status===412||unknown){
        this.needsRefresh=true;
        if(this.action){this.action.conflict=true;this.action.latest=null;}
      }
      if(unknown){
        this.uncertain=kind;
        this.error='変更結果を確認できません。自動再送はしていません。最新一覧を取得して結果を確認してください。';
        if(kind==='password'){
          this.passkeys=[];this.action=null;this.loaded=false;
          this.onRequireLogin('パスワード変更の結果を確認できません。自動再送はしていません。新しいパスワードでログインし、変更されていなければ元のパスワードを確認してください。');
        }
      }
    }finally{this.busy=false;this.controller=null;this.notify();}
    // 保存直後の一覧更新だけは、その操作の成功通知を残す。
    if(succeeded&&kind!=='password')await this.load({keepNotice:true});
    return succeeded;
  }
  async changePassword(password,confirmation,method,current){
    if(this.locked||this.action)return false;
    const error=passwordError(password,confirmation);if(error){this.error=error;this.notify();return false;}
    return this.execute('password',async submitted=>{
      const verification=await this.verify(method,current,'password');
      submitted();await this.request('/account/password',{method:'POST',body:JSON.stringify({newPassword:password,verification})});
    },'');
  }
  async add(name,method,current){
    if(this.locked||this.action)return false;
    const error=passkeyNameError(name);if(error){this.error=error;this.notify();return false;}
    return this.execute('add',async submitted=>{
      const verification=await this.verify(method,current,'passkey-add');
      const options=await this.request('/account/passkeys/options',{method:'POST',body:JSON.stringify({verification})});
      const credential=await this.browser.creation(options.publicKeyOptions,this.controller.signal);
      submitted();await this.request('/account/passkeys',{method:'POST',body:JSON.stringify({challengeId:options.challengeId,credential,displayName:name})});
    },'パスキーを登録しました。');
  }
  async rename(){
    if(this.locked||this.action?.kind!=='rename'||this.action.conflict)return false;
    const action=this.action,error=passkeyNameError(action.name);if(error){this.error=error;this.notify();return false;}
    return this.execute('rename',async submitted=>{
      submitted();await this.request('/account/passkeys/'+encodeURIComponent(action.original.id),{method:'PUT',headers:{'If-Match':action.original.etag},body:JSON.stringify({displayName:action.name})});
    },'パスキーの名前を変更しました。');
  }
  async remove(method,current){
    if(this.locked||this.action?.kind!=='delete'||!this.action.ack||this.action.conflict)return false;
    const action=this.action;
    return this.execute('delete',async submitted=>{
      // 本人確認で署名カウンターが更新される前の版を、削除対象と一緒に固定する。
      const etag=action.original.etag,id=action.original.id;
      const verification=await this.verify(method,current,'passkey-delete:'+id);
      submitted();await this.request('/account/passkeys/'+encodeURIComponent(id),{method:'DELETE',headers:{'If-Match':etag},body:JSON.stringify(verification)});
    },'パスキーを削除しました。');
  }
}
