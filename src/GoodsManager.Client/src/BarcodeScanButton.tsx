import {useEffect,useId,useRef,useState} from 'react';
import {BarcodeScannerSession,type ScannedBarcode} from './barcode-scanner';

export function BarcodeScanButton({disabled=false,preferredScheme,onRead,label}:{disabled?:boolean;preferredScheme:string;onRead:(value:ScannedBarcode)=>void;label:string}){
  const [open,setOpen]=useState(false),[retry,setRetry]=useState(0),[error,setError]=useState(''),[ready,setReady]=useState(false),[notice,setNotice]=useState('');
  const dialog=useRef<HTMLDialogElement>(null),target=useRef<HTMLDivElement>(null),button=useRef<HTMLButtonElement>(null),callback=useRef(onRead),id=useId();
  callback.current=onRead;
  useEffect(()=>{
    if(!open)return;
    if(disabled){setOpen(false);return;}
    const current=dialog.current!;current.showModal();setReady(false);setError('');
    if(!window.isSecureContext||!navigator.mediaDevices?.getUserMedia){setError('カメラ読み取りにはHTTPSと対応するブラウザが必要です。');return()=>current.close();}
    let active=true;
    const session=new BarcodeScannerSession({target:target.current!,preferredScheme,onReady:()=>{if(active)setReady(true);},onError:message=>{if(active){setError(message);setReady(false);}},onRead:value=>{if(!active)return;callback.current(value);setNotice('バーコードを読み取りました。コード欄の内容を確認してください。');setOpen(false);}});
    // ページの離脱・バックグラウンド移行では、Reactの後処理を待たず映像を止める。
    const close=()=>{active=false;session.cancel();setOpen(false);};
    const hide=()=>{if(document.hidden)close();};
    addEventListener('pagehide',close);addEventListener('popstate',close);addEventListener('app:navigate',close);document.addEventListener('visibilitychange',hide);
    void session.start();
    return()=>{active=false;session.cancel();current.close();removeEventListener('pagehide',close);removeEventListener('popstate',close);removeEventListener('app:navigate',close);document.removeEventListener('visibilitychange',hide);};
  },[open,retry,disabled,preferredScheme]);
  useEffect(()=>{if(!open&&notice)button.current?.focus();},[open,notice]);
  return <><button type="button" ref={button} className="btn small" disabled={disabled} aria-label={`${label}をカメラで読み取る`} onClick={()=>{setNotice('');setOpen(true);}}>カメラで読み取る</button><span className="barcode-scan-status" role="status">{notice}</span><dialog ref={dialog} className="barcode-dialog" aria-labelledby={id+'-title'} aria-describedby={id+'-help'} onCancel={event=>{event.preventDefault();setOpen(false);}}><h2 id={id+'-title'}>バーコードを読み取る</h2><p id={id+'-help'}>バーコード全体を中央に映してください。有効なコードを確認すると、入力欄に反映してカメラを終了します。</p>{preferredScheme.startsWith('isbn')&&<p>本の上段にある、978・979で始まるバーコードを映してください。13桁の本の番号として入力します。</p>}<div ref={target} className="barcode-preview"><video autoPlay muted playsInline aria-label="バーコード読み取り用カメラ"/></div>{error?<p role="alert" className="error-message">{error}</p>:<p role="status">{ready?'読み取り中…':'カメラの利用許可を確認して、映像の開始を待っています…'}</p>}<div className="barcode-dialog-actions">{error&&<button type="button" className="btn" onClick={()=>setRetry(n=>n+1)}>もう一度試す</button>}<button type="button" className="btn quiet" onClick={()=>setOpen(false)}>読み取りをやめる</button></div></dialog></>;
}
