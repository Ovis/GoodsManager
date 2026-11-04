import {useEffect,useSyncExternalStore} from 'react';
import type {CollectionsStore} from './CollectionsStore';

/** 独立したコレクションの作成と、範囲を確認した全体削除を行う。 */
export function CollectionsManager({store,current,onOpen}:{store:CollectionsStore;current:string;onOpen:(id:string)=>void}){
    useSyncExternalStore(store.subscribe,store.snapshot);
    const preparing=store.items.some(x=>x.sampleStatus==='pending');
    useEffect(()=>{
        if(!preparing)return;

        const timer=setInterval(()=>{
            if(!store.loading)void store.load();
        },2000);
        return()=>clearInterval(timer);
    },[store,preparing]);

    return <div className="collections-manager">
        <span className="eyebrow">COLLECTIONS</span><h1>コレクション管理</h1>
        <p>商品・所持品・マスター・画像を、コレクションごとに分けて管理します。</p>
        {store.error&&<p role="alert">{store.error}</p>}
        {store.notice&&<p role="status">{store.notice}</p>}
        {store.save.state==='unknown'&&<button className="btn" onClick={()=>void store.replay()}>同じ要求で結果を確認</button>}
        <section className="panel">
            <h2>新しいコレクション</h2>
            <label className="field">コレクション名<input value={store.name} disabled={store.locked} onChange={e=>{
                store.name=e.target.value;
                store.notify();
            }}/></label>
            <label className="field">初期設定<select aria-label="初期設定" value={store.importFrom} disabled={store.locked} onChange={e=>{
                store.importFrom=e.target.value;
                store.ack=false;
                if(store.importFrom==='@samples'&&!store.name)store.name='サンプルコレクション';

                store.notify();
            }}>
                <option value="">空で作成</option><option value="@samples">サンプルデータ入りで作成</option>
                {store.items.filter(x=>x.sampleStatus!=='pending'&&x.sampleStatus!=='failed').map(x=><option key={x.id} value={x.id}>「{x.name}」の設定をコピー</option>)}
            </select></label>
            {store.importFrom&&store.importFrom!=='@samples'&&<div className="note">
                <p>カテゴリの階層と並び順、作品・シリーズ・キャラクター・タグ・メーカー・販売元と関連、画像変換設定をコピーします。コピー後の設定は独立し、元の変更には追従しません。</p>
                <p>商品・所持品・写真はコピーしません。</p>
                <label><input type="checkbox" checked={store.ack} disabled={store.locked} onChange={e=>{
                    store.ack=e.target.checked;
                    store.notify();
                }}/>コピーする設定を確認しました</label>
            </div>}
            {store.importFrom==='@samples'&&<p className="muted">架空の商品64件と画像・関連マスターを追加します。既存のコレクションは変更しません。</p>}
            <button className="btn primary" disabled={store.locked} onClick={()=>void store.submit()}>コレクションを作成</button>
        </section>
        <section className="panel">
            <h2>参加中のコレクション</h2>
            <button className="btn small quiet" disabled={store.loading} onClick={()=>void store.load()}>一覧を再取得</button>
            {store.items.map(x=><article className="collection-space" key={x.id}>
                <h3>{x.name}{current===x.id?'（選択中）':''}</h3>
                {x.sampleStatus==='pending'&&<p role="status">サンプルを準備しています。</p>}
                {x.sampleStatus==='failed'&&<div role="alert">
                    <p>サンプルを準備できませんでした。アカウントと他のコレクションはそのまま利用できます。</p>
                    {x.role==='manager'&&<button className="btn" disabled={store.locked} onClick={()=>void store.retrySamples(x)}>サンプル作成を再試行</button>}
                </div>}
                <p>{x.role==='manager'?'コレクション管理者':'編集者'}</p>
                <div className="editor-actions">
                    <button className="btn" disabled={x.sampleStatus==='pending'||x.sampleStatus==='failed'} onClick={()=>onOpen(x.id)}>開く</button>
                    {x.role==='manager'&&<>
                        <button className="btn quiet" disabled={store.locked} onClick={()=>store.edit(x)}>名前を変更</button>
                        <button className="btn danger quiet" disabled={store.locked||store.reviewing} onClick={()=>void store.reviewDelete(x)}>削除</button>
                    </>}
                </div>
                {store.deletion?.id===x.id&&<div className="collection-delete note">
                    <h3>コレクション全体を削除</h3>
                    <p>商品 {store.deletion.products}件・ゴミ箱 {store.deletion.trashedProducts}件・所持ロット {store.deletion.lots}件・個体 {store.deletion.items}件・画像の関連 {store.deletion.images}件・マスター {store.deletion.masters}件</p>
                    {store.deletion.canDelete?<>
                        <p>このコレクションの商品・所持品・マスター・画像をすべて削除します。編集中の入力も破棄されます。この操作は取り消せません。</p>
                        <label className="field">確認のため「{x.name}」と入力<input value={store.deleteName} disabled={store.locked} autoComplete="off" onChange={e=>{
                            store.deleteName=e.target.value;
                            store.notify();
                        }}/></label>
                        <button className="btn danger" disabled={store.locked||store.deleteName!==store.deletion.name} onClick={()=>void store.remove()}>コレクションを削除</button>
                    </>:<p role="alert">最後のコレクションは削除できません。別のコレクションを作成してください。他の参加者についても最後の1件は保護されます。</p>}
                    <button className="btn quiet" disabled={store.locked} onClick={()=>{
                        store.deletion=null;
                        store.deleteName='';
                        store.notify();
                    }}>キャンセル</button>
                </div>}
                {store.rename?.id===x.id&&<div className="collection-rename">
                    <label className="field">新しい名前<input value={store.rename.name} disabled={store.locked} onChange={e=>{
                        if(store.rename)store.rename.name=e.target.value;

                        store.notify();
                    }}/></label>
                    <div className="editor-actions">
                        {store.conflict?<button className="btn" onClick={()=>void store.review()}>最新の名前を取得して見直す</button>:<button className="btn" disabled={store.locked} onClick={()=>void store.submit(true)}>名前を保存</button>}
                        <button className="btn quiet" disabled={store.locked} onClick={()=>{
                            store.rename=null;
                            store.conflict=false;
                            store.notify();
                        }}>変更を取り消す</button>
                    </div>
                </div>}
            </article>)}
        </section>
    </div>;
}
