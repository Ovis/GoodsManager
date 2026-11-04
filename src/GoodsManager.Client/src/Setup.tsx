import { useState } from 'react';
import { api, ApiError, errorText } from './api';
import { validText } from './registration-state.mjs';

/** 最初のログイン用アカウントを作成する。秘密は画面の入力状態だけに保持する。 */
export function Setup({onSuccess,onRecheck}:{onSuccess:(withSamples:boolean)=>Promise<void>;onRecheck:(notice:string)=>void}) {
    const [setupToken,setSetupToken]=useState(''),[loginId,setLoginId]=useState(''),[password,setPassword]=useState(''),[confirmation,setConfirmation]=useState('');
    const [busy,setBusy]=useState(false),[error,setError]=useState(''),[checkRequired,setCheckRequired]=useState(false);
    const [withSamples,setWithSamples]=useState(false);
    const run=async()=>{
        if(busy||checkRequired)return;

        setError('');
        if(!/^[0-9a-f]{64}$/.test(setupToken)){
            setError('初期登録トークンを入力してください。');
            return;
        }

        if(!/^[A-Za-z0-9._-]{1,64}$/.test(loginId)){
            setError('ログインIDは英数字・.・_・-の1～64文字で入力してください。');
            return;
        }

        if(!validText(password,128)||[...password].length<12){
            setError('パスワードは12～128文字で入力してください。NULや不正な文字は使えません。');
            return;
        }

        if(password!==confirmation){
            setError('確認用パスワードが一致しません。');
            return;
        }

        setBusy(true);
        try{
            await api('/setup',{method:'POST',body:JSON.stringify({loginId,password,setupToken,createSampleCollection:withSamples})});
            setPassword('');
            setConfirmation('');
            setSetupToken('');
            await onSuccess(withSamples);
        }catch(e){
            if(e instanceof ApiError&&e.code==='SETUP_COMPLETED'){
                setPassword('');
                setConfirmation('');
                setSetupToken('');
                onRecheck('初期登録はすでに完了しています。登録済みのアカウントでログインしてください。');
            }else if(e instanceof ApiError&&e.code==='SETUP_TOKEN_INVALID'){
                setError('初期登録トークンが正しくないか、有効期限が切れています。サーバーの保存先を確認してください。');
            }else if(e instanceof ApiError&&e.status===400){
                setError('ログインIDとパスワードの入力条件を確認してください。');
            }else{
                // 登録が確定した後に応答やセッション取得が失敗した可能性があるため、再送より先に状態を確認する。
                setError(`${errorText(e)} 登録が完了している可能性があります。登録状態を確認してください。`);
                setCheckRequired(true);
            }
        }finally{
            setBusy(false);
        }
    };

    return <section className="login-panel panel"><span className="eyebrow">WELCOME</span><h1>初期登録</h1><p>Goods Managerへようこそ。ログインに使うアカウントを作成します。</p><form onSubmit={e=>{
        e.preventDefault();
        void run();
    }}><label className="field">初期登録トークン<input type="password" name="setupToken" autoComplete="off" autoCapitalize="none" spellCheck={false} maxLength={64} value={setupToken} onChange={e=>setSetupToken(e.target.value)} required disabled={busy||checkRequired} aria-describedby="setup-token-help"/><small id="setup-token-help">サーバーのデータ保存先にある setup-token.txt の1行目を入力してください。有効期限は24時間です。</small></label><label className="field">ログインID<input name="username" autoComplete="username" autoCapitalize="none" spellCheck={false} value={loginId} onChange={e=>setLoginId(e.target.value)} required disabled={busy||checkRequired} aria-describedby="setup-id-help"/><small id="setup-id-help">英数字・.・_・-の1～64文字。大小文字は区別しません。</small></label><label className="field">パスワード<input type="password" name="password" autoComplete="new-password" value={password} onChange={e=>setPassword(e.target.value)} required disabled={busy||checkRequired} aria-describedby="setup-password-help"/><small id="setup-password-help">12～128文字で入力してください。</small></label><label className="field">パスワード確認<input type="password" name="passwordConfirmation" autoComplete="new-password" value={confirmation} onChange={e=>setConfirmation(e.target.value)} required disabled={busy||checkRequired}/></label><label className="setup-samples"><input type="checkbox" checked={withSamples} disabled={busy||checkRequired} onChange={e=>setWithSamples(e.target.checked)}/>サンプル入りのコレクションを作成する</label><p className="muted">空のマイコレクションとは別に、架空の商品・画像が入ったコレクションを用意します。後から追加・削除できます。</p><button className="btn primary" disabled={busy||checkRequired}>アカウントを作成</button></form>{busy&&<p role="status">アカウントを作成中…</p>}{error&&<p role="alert" className="error-message">{error}</p>}{checkRequired&&<button className="btn" type="button" onClick={()=>onRecheck('登録状態を確認しました。アカウントが作成済みの場合はログインしてください。')}>登録状態を確認</button>}</section>;
}
