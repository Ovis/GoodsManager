import {useEffect,useRef,useState,useSyncExternalStore} from 'react';
import type {PhotoOcrSession} from './PhotoOcrSession';
import {copyPhotoText} from './clipboard';

const labels={queued:'読み取り待ち',preparing:'文字読み取りを準備中',recognizing:'文字読み取り中',success:'文字あり',empty:'文字を検出できませんでした',error:'読み取り失敗',paused:'一時停止中'};

export function PhotoOcrDialog({session}:{session:PhotoOcrSession}){
    useSyncExternalStore(session.subscribe,session.snapshot);

    const dialog=useRef<HTMLDialogElement>(null),text=useRef<HTMLTextAreaElement>(null),previousFocus=useRef<HTMLElement|null>(null);
    const [copied,setCopied]=useState('');
    const entry=session.selected;
    useEffect(()=>{
        const target=dialog.current;
        if(!target)return;

        if(session.open){
            if(document.querySelector('dialog[open]')!==target&&document.querySelector('dialog[open]')){
                session.close();
                return;
            }

            previousFocus.current=document.activeElement instanceof HTMLElement?document.activeElement:null;
            if(!target.open)target.showModal();
        }else if(target.open){
            target.close();
            if(previousFocus.current?.isConnected)previousFocus.current.focus();
        }
    },[session.open]);
    useEffect(()=>setCopied(''),[entry?.photo.localId,entry?.text]);
    useEffect(()=>()=>{
        dialog.current?.close();
    },[]);

    const copy=async()=>{
        if(!entry?.text)return;

        setCopied(await copyPhotoText(entry.text,navigator.clipboard,text.current));
    };

    return <dialog ref={dialog} className="registration-dialog photo-ocr-dialog" aria-labelledby="photo-ocr-heading" onCancel={event=>{
        event.preventDefault();
        session.close();
    }}>
    <div className="section-heading"><h2 id="photo-ocr-heading">写真の読み取り結果</h2><button type="button" className="btn quiet" onClick={()=>session.close()}>閉じる</button></div>
    <p>読み取った文字は誤りや抜けがある場合があります。内容を確認し、必要な部分をコピーして商品名などに貼り付けてください。</p>
    <div className="photo-ocr-choices" role="group" aria-label="読み取る写真を選択">{session.entries.map((photo,index)=><button type="button" className={`btn small ${photo===entry?'primary':''}`} key={photo.photo.localId} aria-pressed={photo===entry} onClick={()=>{
        session.selectedId=photo.photo.localId;
        session.notify();
    }}>写真{index+1}：{labels[photo.state]}</button>)}</div>
    {entry&&<><div className="photo-ocr-content"><figure><img src={entry.photo.url} alt={`読み取り元の写真 ${session.entries.indexOf(entry)+1}`}/><figcaption>{entry.photo.file.name}</figcaption></figure><div className="field"><label htmlFor="photo-ocr-text">読み取った文字（編集不可）</label><textarea ref={text} id="photo-ocr-text" rows={12} readOnly value={entry.text}/><p role="status">{labels[entry.state]}</p>{entry.error&&<p role="alert">{entry.error}</p>}<div className="editor-actions"><button type="button" className="btn" disabled={!entry.text} onClick={()=>void copy()}>全文コピー</button>{['error','empty','paused'].includes(entry.state)&&<button type="button" className="btn" onClick={()=>session.retry(entry.photo.localId)}>この写真を再試行</button>}</div><p role="status">{copied}</p></div></div></>}
  </dialog>;
}
