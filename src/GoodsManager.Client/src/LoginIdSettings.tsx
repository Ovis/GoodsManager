import {useEffect,useSyncExternalStore} from 'react';
import type {LoginIdStore} from './LoginIdStore';

export function LoginIdSettings({store,disabled=false}:{store:LoginIdStore;disabled?:boolean}){
    useSyncExternalStore(store.subscribe,store.snapshot);
    useEffect(()=>{
        void store.load();
    },[store]);

    return <section className="panel account-login-id"><h2>ログインIDを変更</h2><p>現在のログイン状態と登録済みパスキーは維持します。次回から新しいIDでログインしてください。</p>{store.error&&<p role="alert" className="error-message">{store.error}</p>}{store.notice&&<p role="status" className="success-message">{store.notice}</p>}<form className="account-form" onSubmit={event=>{
        event.preventDefault();
        void store.submit();
    }}><fieldset disabled={disabled||store.locked||!store.current}><label className="field">新しいログインID<input autoComplete="username" minLength={1} maxLength={64} pattern="[A-Za-z0-9._-]+" required value={store.name} onChange={event=>store.update(event.target.value)}/><small>英数字・ピリオド・アンダースコア・ハイフンの1〜64文字。大小文字は区別しません。</small></label><div className="editor-actions"><button className="btn primary" disabled={store.conflict||!store.dirty}>ログインIDを保存</button><button type="button" className="btn quiet" onClick={()=>store.reset()}>入力を元に戻す</button></div></fieldset></form><button type="button" className="btn" disabled={disabled||store.busy} onClick={()=>void store.load()}>最新のログインIDを確認</button>{store.conflict&&<p className="note">保存内容が変更されたか、結果の確認が必要です。入力は保持しています。最新のログインIDを取得して見直してください。</p>}{store.latest&&<div className="note"><p>最新のログインID：<strong>{store.latest.loginId}</strong></p><p>編集中の入力：{store.name}</p><button className="btn" disabled={disabled||store.locked} onClick={()=>store.reviewed()}>比較しました。入力を保持して見直す</button></div>}</section>;
}
