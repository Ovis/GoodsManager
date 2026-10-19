import { useState } from 'react';
import { api, ApiError, errorText } from './api';
import { validText } from './registration-state.mjs';

/** 最初のログイン用アカウントを作成する。秘密は画面の入力状態だけに保持する。 */
export function Setup({onSuccess,onRecheck}:{onSuccess:()=>Promise<void>;onRecheck:(notice:string)=>void}) {
  const [loginId,setLoginId]=useState(''),[password,setPassword]=useState(''),[confirmation,setConfirmation]=useState('');
  const [busy,setBusy]=useState(false),[error,setError]=useState(''),[checkRequired,setCheckRequired]=useState(false);
  const run=async()=>{
    if(busy||checkRequired)return;
    setError('');
    if(!/^[A-Za-z0-9._-]{1,64}$/.test(loginId)){setError('ログインIDは英数字・.・_・-の1～64文字で入力してください。');return;}
    if(!validText(password,128)||[...password].length<12){setError('パスワードは12～128文字で入力してください。NULや不正な文字は使えません。');return;}
    if(password!==confirmation){setError('確認用パスワードが一致しません。');return;}
    setBusy(true);
    try{
      await api('/setup',{method:'POST',body:JSON.stringify({loginId,password})});
      setPassword('');setConfirmation('');
      await onSuccess();
    }catch(e){
      if(e instanceof ApiError&&e.code==='SETUP_COMPLETED'){
        setPassword('');setConfirmation('');
        onRecheck('初期登録はすでに完了しています。登録済みのアカウントでログインしてください。');
      }else if(e instanceof ApiError&&e.status===400){
        setError('ログインIDとパスワードの入力条件を確認してください。');
      }else{
        // 登録が確定した後に応答やセッション取得が失敗した可能性があるため、再送より先に状態を確認する。
        setError(`${errorText(e)} 登録が完了している可能性があります。登録状態を確認してください。`);
        setCheckRequired(true);
      }
    }finally{setBusy(false);}
  };
  return <section className="login-panel panel"><span className="eyebrow">WELCOME</span><h1>初期登録</h1><p>Goods Managerへようこそ。ログインに使うアカウントを作成します。</p><form onSubmit={e=>{e.preventDefault();void run();}}><label className="field">ログインID<input name="username" autoComplete="username" autoCapitalize="none" spellCheck={false} value={loginId} onChange={e=>setLoginId(e.target.value)} required disabled={busy||checkRequired} aria-describedby="setup-id-help"/><small id="setup-id-help">英数字・.・_・-の1～64文字。大小文字は区別しません。</small></label><label className="field">パスワード<input type="password" name="password" autoComplete="new-password" value={password} onChange={e=>setPassword(e.target.value)} required disabled={busy||checkRequired} aria-describedby="setup-password-help"/><small id="setup-password-help">12～128文字で入力してください。</small></label><label className="field">パスワード確認<input type="password" name="passwordConfirmation" autoComplete="new-password" value={confirmation} onChange={e=>setConfirmation(e.target.value)} required disabled={busy||checkRequired}/></label><button className="btn primary" disabled={busy||checkRequired}>アカウントを作成</button></form>{busy&&<p role="status">アカウントを作成中…</p>}{error&&<p role="alert" className="error-message">{error}</p>}{checkRequired&&<button className="btn" type="button" onClick={()=>onRecheck('登録状態を確認しました。アカウントが作成済みの場合はログインしてください。')}>登録状態を確認</button>}</section>;
}
