import {api,ApiError} from './api';
import type {RegistrationStore} from './RegistrationStore';
import {codeError,validText} from './registration-state.mjs';

export type PublisherMatch={id:string;name:string;websiteUrl:string|null;memo:string|null;createdAt:string};
export type BookPublisher={name:string;matches:PublisherMatch[]};
export type BookMetadata={title:string;volume:string;edition:string;authors:string[];publishers:BookPublisher[];publicationYear:string;sourceUrl:string;inProcess:boolean};
type Transport=<T>(path:string,options?:RequestInit)=>Promise<T>;
export function validIsbn(code:{scheme:string;value:string}|undefined){return !!code&&['isbn10','isbn13'].includes(code.scheme)&&!!code.value&&validText(code.value,200)&&!codeError(code.scheme,code.value);}
const keyOf=(store:RegistrationStore,index:number)=>{const value=store.draft.identifiers[index];return value?value.scheme+'\0'+value.value:'';};

/** 検索と反映を保存から分離し、応答時点の空欄だけを補完する。 */
export class BookLookupStore{
  busy=false;error='';notice='';candidates:BookMetadata[]=[];publisherBook:BookMetadata|null=null;
  private generation=0;private controller:AbortController|null=null;private inputKey:string;
  private active=true;private version=0;private listeners=new Set<()=>void>();
  constructor(private store:RegistrationStore,private index:number,private disabled:()=>boolean=()=>false,private transport:Transport=api){this.inputKey=keyOf(store,index);}
  subscribe=(fn:()=>void)=>{this.listeners.add(fn);return()=>this.listeners.delete(fn);};snapshot=()=>this.version;
  private notify(){this.version++;for(const fn of this.listeners)fn();}
  private blocked(){return this.store.blocked||this.disabled();}
  connect(){this.active=true;const unsubscribe=this.store.subscribe(()=>{if(this.inputKey!==keyOf(this.store,this.index)||this.blocked()&&(this.busy||this.candidates.length>0))this.cancel();});return()=>{this.active=false;unsubscribe();this.cancel();};}
  cancel(){this.generation++;this.controller?.abort();this.controller=null;this.inputKey=keyOf(this.store,this.index);this.busy=false;this.candidates=[];this.publisherBook=null;this.error='';this.notice='';this.notify();}
  private current(generation:number,key:string){return this.active&&generation===this.generation&&key===keyOf(this.store,this.index)&&!this.blocked();}
  canApplyPublisher=()=>this.current(this.generation,this.inputKey)&&!this.store.draft.manufacturerId&&!!this.publisherBook;

  async search(){
    if(this.busy||this.blocked())return;
    this.cancel();const code=this.store.draft.identifiers[this.index];
    if(!validIsbn(code)){this.error='本の番号の種類・桁数・チェックディジットを確認してください。';this.notify();return;}
    const generation=this.generation,key=this.inputKey,controller=new AbortController();this.controller=controller;this.busy=true;this.notify();
    try{
      const result=await this.transport<{items:BookMetadata[]}>('/book-metadata?isbn='+encodeURIComponent(code.value),{signal:controller.signal});
      if(!this.current(generation,key))return;
      if(!Array.isArray(result.items))throw new Error('invalid response');
      if(!result.items.length)this.notice='書籍情報が見つかりませんでした。ISBNやほかの入力は保持しています。手入力で続けられます。';
      else if(result.items.length===1)this.apply(result.items[0]);
      else this.candidates=result.items;
    }catch(error){if(this.current(generation,key)&&!controller.signal.aborted)this.error=error instanceof ApiError&&error.status===401?error.message:error instanceof ApiError&&error.code==='INVALID_ISBN'?'本の番号の種類・桁数・チェックディジットを確認してください。':'書籍情報を取得できませんでした。入力は保持しています。もう一度取得するか、手入力で続けてください。';}
    finally{if(generation===this.generation){this.busy=false;this.controller=null;this.notify();}}
  }
  choose(book:BookMetadata){if(!this.candidates.includes(book)||!this.current(this.generation,this.inputKey))return;this.candidates=[];this.apply(book);this.notify();}
  dismiss(){this.candidates=[];this.notice='書籍情報の選択を取り消しました。入力は変更していません。';this.notify();}
  private apply(book:BookMetadata){
    if(!this.current(this.generation,this.inputKey))return;
    const skipped:string[]=[];
    if(!this.store.draft.name){if(validText(book.title,200))this.store.update('name',book.title);else skipped.push('書名が入力上限を超えるため、商品名は手入力してください。');}
    const year=book.publicationYear, memo=[book.authors.length?'著者：'+book.authors.join('、'):'',year?'出版年：'+year+(/^\d{4}$/.test(year)?'年':''):''].filter(Boolean).join('\n');
    if(!this.store.draft.memo&&memo){if(validText(memo,4000))this.store.update('memo',memo);else skipped.push('著者・出版年が入力上限を超えるため、商品メモは手入力してください。');}
    const links=[...this.store.draft.links];
    if(!links.some(x=>x.trim()===book.sourceUrl)){
      const empty=links.findIndex(x=>!x);if(empty>=0){links[empty]=book.sourceUrl;this.store.update('links',links);}
      else if(links.length<100)this.store.update('links',[...links,book.sourceUrl]);else skipped.push('参考リンクが100件あるため、出典リンクは追加していません。');
    }
    this.publisherBook=book;
    if(!this.store.draft.manufacturerId&&book.publishers.length===1&&book.publishers[0].matches.length===1)this.choosePublisher(book.publishers[0].matches[0]);
    this.notice=(book.inProcess?'作成中の書誌情報を使用しました。後日修正される場合があります。':'書籍情報を取得しました。')+'入力済みの項目は保持し、空欄だけに反映しました。'+skipped.join('');
  }
  choosePublisher(match:PublisherMatch){
    if(!this.canApplyPublisher()||!this.publisherBook!.publishers.some(x=>x.matches.some(v=>v.id===match.id)))return;
    this.store.resolved({[match.id]:match.name});this.store.update('manufacturerId',match.id);this.notify();
  }
}
