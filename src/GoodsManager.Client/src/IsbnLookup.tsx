import {useEffect,useMemo,useRef,useSyncExternalStore} from 'react';
import {BookLookupStore,validIsbn} from './BookLookupStore';
import {RegistrationPicker} from './RegistrationPicker';
import type {RegistrationStore} from './RegistrationStore';

export function IsbnLookup({store,index,disabled=false,isBlocked}:{store:RegistrationStore;index:number;disabled?:boolean;isBlocked?:()=>boolean}){
  const blocked=useRef(isBlocked);blocked.current=isBlocked;
  const lookup=useMemo(()=>new BookLookupStore(store,index,()=>blocked.current?.()??false),[store,index]);
  useSyncExternalStore(lookup.subscribe,lookup.snapshot);
  useEffect(()=>lookup.connect(),[lookup]);
  useEffect(()=>{if(disabled&&(lookup.busy||lookup.candidates.length))lookup.cancel();},[disabled,lookup]);
  const modal=useRef<HTMLDialogElement>(null),button=useRef<HTMLButtonElement>(null);
  useEffect(()=>{const current=modal.current;if(!current)return;if(lookup.candidates.length){current.showModal();return()=>current.close();}},[lookup.candidates]);
  useEffect(()=>{const cancel=()=>lookup.cancel();addEventListener('app:navigate',cancel);addEventListener('popstate',cancel);addEventListener('pagehide',cancel);return()=>{removeEventListener('app:navigate',cancel);removeEventListener('popstate',cancel);removeEventListener('pagehide',cancel);};},[lookup]);
  const code=store.draft.identifiers[index];
  if(!code||!['isbn10','isbn13'].includes(code.scheme))return null;
  const publisher=lookup.publisherBook;
  const dismiss=()=>{lookup.dismiss();button.current?.focus();};
  return <div className="isbn-lookup">
    <button type="button" className="btn small" ref={button} disabled={disabled||lookup.busy||!validIsbn(code)} onClick={()=>void lookup.search()}>ISBNから書籍情報を取得</button>
    {!validIsbn(code)&&<p className="muted">有効な本の番号を入力すると取得できます。</p>}
    <p className="muted isbn-attribution"><a href="https://ndlsearch.ndl.go.jp/help/api" target="_blank" rel="noreferrer">国立国会図書館サーチAPI</a>を使用し、国立国会図書館作成の書誌情報を入力用に整形しています。取得は商品の保存とは別の操作です。</p>
    {lookup.busy&&<p role="status">書籍情報を取得中… ほかの項目は引き続き入力できます。</p>}
    {lookup.notice&&<p role="status">{lookup.notice}</p>}
    {lookup.error&&<p role="alert">{lookup.error} <button type="button" className="btn small" disabled={disabled||lookup.busy} onClick={()=>void lookup.search()}>もう一度取得</button></p>}
    {publisher&&<p><a className="reference-link" href={publisher.sourceUrl} target="_blank" rel="noreferrer">出典：NDLサーチの資料詳細</a></p>}
    {publisher&&!store.draft.manufacturerId&&publisher.publishers.length>0&&<div className="isbn-publishers"><h3>出版社を製造元に設定</h3>{publisher.publishers.map((p,i)=><div key={p.name+'-'+i}>
      <strong>{p.name}</strong>
      {p.matches.length>1&&<p>同名の組織が複数あります。設定する組織を選んでください。</p>}
      {p.matches.map((match,n)=><div className="isbn-publisher-match" key={match.id}><p>{match.name}{p.matches.length>1?`（候補 ${n+1}）`:''}</p>{match.websiteUrl&&<p>{match.websiteUrl}</p>}{match.memo&&<p className="memo">{match.memo}</p>}<small>登録日：{new Date(match.createdAt).toLocaleString('ja-JP')}</small><button type="button" className="btn small" disabled={disabled} onClick={()=>lookup.choosePublisher(match)}>この組織を製造元に設定</button></div>)}
      {!p.matches.length&&<><p>この出版社は未登録です。作成・選択する場合だけ組織マスターに追加します。</p><RegistrationPicker store={store} field="manufacturerId" kind="organizations" label="出版社（製造元）" suggestion={p.name} canSelect={lookup.canApplyPublisher}/></>}
    </div>)}</div>}
    <dialog ref={modal} className="isbn-dialog" aria-label="書籍情報の候補を選択" onCancel={e=>{e.preventDefault();dismiss();}}><h2>書籍情報を選択</h2><p>同じISBNの書誌が複数あります。使用する書誌を選んでください。</p><div className="isbn-candidates">{lookup.candidates.map((book,i)=><article key={book.sourceUrl+'-'+i}><h3>{book.title}</h3>{book.volume&&<p>巻次：{book.volume}</p>}{book.edition&&<p>版：{book.edition}</p>}{book.authors.length>0&&<p>著者：{book.authors.join('、')}</p>}{book.publishers.length>0&&<p>出版社：{book.publishers.map(x=>x.name).join('、')}</p>}{book.publicationYear&&<p>出版年：{book.publicationYear}</p>}{book.inProcess&&<p>作成中の書誌情報</p>}<a className="reference-link" href={book.sourceUrl} target="_blank" rel="noreferrer">NDLサーチで確認</a><button type="button" className="btn" onClick={()=>{lookup.choose(book);button.current?.focus();}}>この書誌を使用</button></article>)}</div><footer><button type="button" className="btn quiet" onClick={dismiss}>選択を取り消す</button></footer></dialog>
  </div>;
}
