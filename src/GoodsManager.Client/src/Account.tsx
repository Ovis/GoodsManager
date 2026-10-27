import { useEffect, useState, useSyncExternalStore } from 'react';
import { AccountState } from './account-state.mjs';
import { passkeyCapabilities } from './passkey-browser';

type Proof = {method:string;password:string};
type ProofProps = {id:string;loginId:string;value:Proof;onChange:(proof:Proof)=>void;disabled:boolean;hasPasskeys:boolean};

/** 操作ごとに本人確認し、パスワードを別のフォームへ流用しない。 */
function Verification({id,loginId,value,onChange,disabled,hasPasskeys}:ProofProps) {
  const available=hasPasskeys&&passkeyCapabilities().assertion;
  useEffect(()=>{if(value.method==='passkey'&&!available)onChange({method:'password',password:''});},[available,value.method]);
  return <fieldset className="account-verification" disabled={disabled}>
    <legend>本人確認</legend>
    <input type="hidden" name="username" autoComplete="username" value={loginId}/>
    <label className="field" htmlFor={id+'-method'}>本人確認方法
      <select id={id+'-method'} value={value.method} onChange={e=>onChange({method:e.target.value,password:''})}>
        <option value="password">現在のパスワード</option><option value="passkey" disabled={!available}>登録済みパスキー</option>
      </select>
    </label>
    {value.method==='password'?<label className="field" htmlFor={id+'-current'}>現在のパスワード
      <input id={id+'-current'} name="currentPassword" type="password" autoComplete="current-password" value={value.password} onChange={e=>onChange({...value,password:e.target.value})} required/>
    </label>:<p>操作ボタンを押すと、端末のパスキーで本人確認します。</p>}
  </fieldset>;
}

function PasswordForm({store,loginId}:{store:AccountState;loginId:string}) {
  const [password,setPassword]=useState(''),[confirmation,setConfirmation]=useState('');
  const [proof,setProof]=useState<Proof>({method:'password',password:''});
  const disabled=store.locked||!!store.action;
  return <section className="panel" aria-labelledby="password-heading"><h2 id="password-heading">パスワード変更</h2>
    <p>変更すると、現在の端末を含むすべての端末でログインが解除されます。登録済みパスキーは保持します。</p>
    <form className="account-form" aria-labelledby="password-heading" onSubmit={e=>{e.preventDefault();void (async()=>{
      try{await store.changePassword(password,confirmation,proof.method,proof.password);}finally{setProof(value=>({...value,password:''}));}
    })();}}>
      <fieldset disabled={disabled}>
        <Verification id="password" loginId={loginId} value={proof} onChange={setProof} disabled={disabled} hasPasskeys={store.passkeys.length>0}/>
        <label className="field" htmlFor="account-new-password">新しいパスワード
          <input id="account-new-password" name="newPassword" type="password" autoComplete="new-password" value={password} onChange={e=>setPassword(e.target.value)} required aria-describedby="password-help"/>
          <small id="password-help">12～128文字で入力してください。</small>
        </label>
        <label className="field" htmlFor="account-confirm-password">新しいパスワードの確認
          <input id="account-confirm-password" name="passwordConfirmation" type="password" autoComplete="new-password" value={confirmation} onChange={e=>setConfirmation(e.target.value)} required/>
        </label>
        <button className="btn primary" type="submit">パスワードを変更</button>
      </fieldset>
    </form>
  </section>;
}

function PasskeyAddForm({store,loginId}:{store:AccountState;loginId:string}) {
  const [name,setName]=useState(''),[proof,setProof]=useState<Proof>({method:'password',password:''});
  const available=passkeyCapabilities().creation,disabled=store.locked||!!store.action||!available;
  return <div className="account-add"><h3 id="passkey-add-heading">パスキーを追加</h3>
    {!available&&<p className="note">このブラウザではパスキーを登録できません。HTTPSで、対応ブラウザから開いてください。</p>}
    <form className="account-form" aria-labelledby="passkey-add-heading" onSubmit={e=>{e.preventDefault();void (async()=>{
      try{if(await store.add(name,proof.method,proof.password))setName('');}finally{setProof(value=>({...value,password:''}));}
    })();}}>
      <fieldset disabled={disabled}>
        <label className="field" htmlFor="account-passkey-name">パスキーの名前
          <input id="account-passkey-name" name="passkeyName" autoComplete="off" value={name} onChange={e=>setName(e.target.value)} required aria-describedby="passkey-name-help"/>
          <small id="passkey-name-help">端末などを区別できる名前を1～100文字で入力してください。</small>
        </label>
        <Verification id="passkey-add" loginId={loginId} value={proof} onChange={setProof} disabled={disabled} hasPasskeys={store.passkeys.length>0}/>
        <p>本人確認後に、端末のパスキー登録画面が開きます。登録済みパスキーで本人確認する場合は、端末の操作が2回あります。</p>
        <button className="btn primary" type="submit">本人確認してパスキーを追加</button>
      </fieldset>
    </form>
  </div>;
}

function PasskeyAction({store,loginId}:{store:AccountState;loginId:string}) {
  const [proof,setProof]=useState<Proof>({method:'password',password:''});
  const action=store.action;if(!action)return null;
  const remove=action.kind==='delete',disabled=store.locked||action.conflict;
  return <section className="account-action" aria-labelledby="passkey-action-heading">
    <h3 id="passkey-action-heading">{remove?'パスキーを削除':'パスキーの名前を変更'}</h3>
    <p>対象：<strong>{action.original.displayName}</strong></p>
    {action.conflict&&<div className="note"><p>編集中の内容を保持しています。最新一覧を取得し、対象を見直してください。</p>
      {!store.needsRefresh&&(action.latest?<><p>最新の名前：<strong>{action.latest.displayName}</strong></p><button className="btn" disabled={store.busy} onClick={()=>{store.review();setProof({method:'password',password:''});}}>最新の内容で見直す</button></>:<p>対象は最新一覧にありません。編集を閉じて一覧を確認してください。</p>)}
    </div>}
    <form className="account-form" aria-labelledby="passkey-action-heading" onSubmit={e=>{e.preventDefault();void (async()=>{
      try{if(remove)await store.remove(proof.method,proof.password);else await store.rename();}finally{setProof(value=>({...value,password:''}));}
    })();}}>
      <fieldset disabled={disabled}>
        {remove?<>
          <p>このサイトで使う登録を削除します。端末・パスワード管理アプリに保存されたパスキーは、必要に応じてそちらでも整理してください。</p>
          {store.passkeys.length===1&&<p className="note">最後のパスキーです。削除後はパスワードでログインできます。</p>}
          <Verification id="passkey-delete" loginId={loginId} value={proof} onChange={setProof} disabled={disabled} hasPasskeys={store.passkeys.length>0}/>
          <label className="registration-ack"><input type="checkbox" checked={action.ack} onChange={e=>{action.ack=e.target.checked;store.notify();}}/>このパスキーの登録を削除することを確認しました</label>
        </>:<label className="field" htmlFor="account-passkey-rename">変更後の名前<input id="account-passkey-rename" value={action.name} onChange={e=>{action.name=e.target.value;store.notify();}} required autoComplete="off"/><small>1～100文字で入力してください。</small></label>}
        <button className={'btn '+(remove?'danger':'primary')} type="submit" disabled={remove&&!action.ack}>{remove?'本人確認して削除':'名前を保存'}</button>
      </fieldset>
      <button className="btn quiet" type="button" disabled={store.busy} onClick={()=>store.close()}>編集を閉じる</button>
    </form>
  </section>;
}

/** 認証の秘密はこの画面のフォームだけに置き、画面を離れたら破棄する。 */
export function Account({store,loginId}:{store:AccountState;loginId:string}) {
  useSyncExternalStore(store.subscribe,store.snapshot);
  useEffect(()=>{window.scrollTo(0,0);void store.load();return()=>store.cancelCeremony();},[store]);
  useEffect(()=>{const before=(event:BeforeUnloadEvent)=>{if(store.busy){event.preventDefault();event.returnValue='';}};addEventListener('beforeunload',before);return()=>removeEventListener('beforeunload',before);},[store]);
  return <div className="account-page management">
    <div className="page-heading"><span className="eyebrow">ACCOUNT</span><h1>アカウント設定</h1><p>ログインID：<strong>{loginId}</strong></p></div>
    {store.error&&<p role="alert" className="error-message">{store.error}</p>}
    {store.notice&&<p role="status" className="success-message">{store.notice}</p>}
    {store.busy&&<p role="status">本人確認・変更処理中…</p>}
    {store.needsRefresh&&<p className="note">変更操作を止めています。「最新一覧を取得」で登録状態を確認してください。</p>}
    <PasswordForm store={store} loginId={loginId}/>
    <section className="panel" aria-labelledby="passkeys-heading"><h2 id="passkeys-heading">登録済みパスキー</h2>
      <p>複数の端末やパスワード管理アプリを登録できます。</p>
      <button className="btn" disabled={store.busy||store.loading} onClick={()=>void store.load()}>最新一覧を取得</button>
      {store.loading&&<p role="status">パスキー一覧を取得中…</p>}
      {store.loaded&&!store.passkeys.length&&<p className="account-empty">パスキーはまだ登録されていません。</p>}
      <ul className="account-passkeys">{store.passkeys.map(entry=><li key={entry.id}><div><strong>{entry.displayName}</strong><small>登録日時：{new Date(entry.createdAt).toLocaleString('ja-JP')}</small></div><div className="management-actions"><button className="btn small" disabled={store.locked||!!store.action} onClick={()=>store.open('rename',entry)}>名前を変更</button><button className="btn small danger" disabled={store.locked||!!store.action} onClick={()=>store.open('delete',entry)}>削除</button></div></li>)}</ul>
      {store.action&&<PasskeyAction key={store.action.kind+':'+store.action.original.id} store={store} loginId={loginId}/>}
      <PasskeyAddForm store={store} loginId={loginId}/>
    </section>
  </div>;
}
