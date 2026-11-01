import type {Photo,RegistrationStore} from '../RegistrationStore';
import {PhotoOcrClient,PhotoOcrError,type OcrClient,type OcrIdentity,type OcrResult} from './PhotoOcrClient';

export type OcrPhoto={identity:OcrIdentity;photo:Photo;state:'queued'|'preparing'|'recognizing'|'success'|'empty'|'error'|'paused';text:string;error:string;metrics?:OcrResult};
/** 登録専用。File/object URLの所有者はRegistrationStoreのまま、派生結果だけを保持する。 */
export class PhotoOcrSession {
  entries:OcrPhoto[]=[];open=false;selectedId='';error='';active=false;checking=false;blocked=false;
  private version=0;private listeners=new Set<()=>void>();private epoch=0;private lifecycle=0;private running:OcrPhoto|null=null;
  private excluded=new Set<string>();private stopStore:()=>void;
  constructor(public store:RegistrationStore,private client:OcrClient=new PhotoOcrClient(store.request)){
    this.epoch=store.draftEpoch;this.stopStore=store.subscribe(()=>this.sync());
  }
  subscribe=(fn:()=>void)=>{this.listeners.add(fn);return()=>{this.listeners.delete(fn);};};snapshot=()=>this.version;
  notify(){this.version++;for(const fn of this.listeners)fn();}
  get selected(){return this.entries.find(x=>x.photo.localId===this.selectedId)||this.entries[0];}
  get withText(){return this.entries.filter(x=>x.state==='success').length;}
  get pending(){return this.entries.some(x=>['queued','preparing','recognizing','paused'].includes(x.state));}
  get status(){
    if(this.checking)return '文字読み取りの利用確認中';
    if(this.running?.state==='preparing')return '文字読み取りを準備中';
    if(this.running)return `文字読み取り中 ${this.entries.indexOf(this.running)+1}/${this.entries.length}`;
    if(this.blocked)return '文字読み取りを準備できませんでした。再試行できます。';
    if(this.pending)return '文字読み取りを一時停止中';
    if(this.entries.length)return `文字あり ${this.withText}/${this.entries.length}枚`;
    return '';
  }
  private identity(photo:Photo):OcrIdentity{return {accountId:this.store.request.accountId||'',collectionId:this.store.request.collectionId||'',draftEpoch:this.epoch,localId:photo.localId,attemptId:0};}
  sync(){
    if(this.epoch!==this.store.draftEpoch){this.clear(false);this.epoch=this.store.draftEpoch;}
    const photos=new Set(this.store.photos.map(x=>x.localId));
    const removed=this.entries.filter(x=>!photos.has(x.photo.localId));
    if(removed.includes(this.running!)){this.client.cancel();this.running=null;}
    this.entries=this.entries.filter(x=>photos.has(x.photo.localId));
    for(const id of this.excluded)if(!photos.has(id))this.excluded.delete(id);
    const fresh=this.store.photos.filter(x=>!this.excluded.has(x.localId)&&!this.entries.some(e=>e.photo.localId===x.localId));
    if(fresh.length&&this.active){
      for(const photo of fresh)this.entries.push({identity:this.identity(photo),photo,state:'queued',text:'',error:''});
    }
    const byPhoto=new Map(this.entries.map(entry=>[entry.photo.localId,entry]));
    this.entries=this.store.photos.flatMap(photo=>{const entry=byPhoto.get(photo.localId);return entry?[entry]:[];});
    if(!this.entries.length)this.close();
    this.notify();void this.pump();
  }
  async activate(){
    if(this.active)return;
    this.active=true;this.checking=true;this.error='';const lifecycle=++this.lifecycle;this.notify();
    try{
      await this.client.authorize();if(!this.active||lifecycle!==this.lifecycle)return;
      for(const entry of this.entries)if(entry.state==='paused')entry.state='queued';
      this.checking=false;this.sync();
    }catch(error){if(lifecycle===this.lifecycle)this.failure(error);}
    finally{if(lifecycle===this.lifecycle){this.checking=false;this.notify();}}
  }
  pause(){
    this.active=false;this.checking=false;++this.lifecycle;this.client.cancel();this.running=null;this.close();
    for(const entry of this.entries)if(['queued','preparing','recognizing'].includes(entry.state))entry.state='paused';
    this.notify();
  }
  resetAuthentication(){this.clear(true);}
  private clear(excludePhotos:boolean){
    ++this.lifecycle;this.client.cancel();this.running=null;this.entries=[];this.open=false;this.selectedId='';this.blocked=false;this.checking=false;this.error='';
    this.excluded=new Set(excludePhotos?this.store.photos.map(x=>x.localId):[]);if(excludePhotos)this.active=false;this.notify();
  }
  private failure(error:unknown){
    if(error instanceof PhotoOcrError&&error.stage==='access'){
      this.resetAuthentication();this.error=error.message;
    }else{
      this.blocked=true;this.error=error instanceof Error?error.message:'文字読み取りを準備できませんでした。';
      for(const entry of this.entries)if(entry.state==='queued')entry.state='paused';
    }
    this.notify();
  }
  private valid(entry:OcrPhoto,attempt:number,lifecycle:number){return this.active&&this.lifecycle===lifecycle&&this.epoch===entry.identity.draftEpoch&&entry.identity.attemptId===attempt&&this.entries.includes(entry)&&this.store.photos.includes(entry.photo);}
  private async pump(){
    if(!this.active||this.checking||this.blocked||this.running)return;
    const entry=this.entries.find(x=>x.state==='queued');if(!entry)return;
    this.running=entry;const lifecycle=this.lifecycle,attempt=entry.identity.attemptId;entry.state='preparing';this.notify();
    try{
      const result=await this.client.recognize(entry.photo.file,{...entry.identity},state=>{if(this.valid(entry,attempt,lifecycle)){entry.state=state;this.notify();}});
      if(!this.valid(entry,attempt,lifecycle))return;
      entry.text=result.text;entry.metrics=result;entry.state=result.text?'success':'empty';entry.error='';
    }catch(error){
      if(!this.valid(entry,attempt,lifecycle))return;
      if(error instanceof PhotoOcrError&&error.stage==='cancelled')entry.state='paused';
      else if(error instanceof PhotoOcrError&&error.stage==='access'){this.failure(error);}
      else{entry.state='error';entry.error=error instanceof Error?error.message:'文字読み取りに失敗しました。';if(error instanceof PhotoOcrError&&error.stage==='prepare')this.failure(error);}
    }finally{if(this.running===entry&&lifecycle===this.lifecycle&&attempt===entry.identity.attemptId)this.running=null;this.notify();void this.pump();}
  }
  close(){this.open=false;this.notify();}
  async show(){
    if(!this.active||this.checking)return;const lifecycle=this.lifecycle;
    try{await this.client.authorize();if(!this.active||lifecycle!==this.lifecycle)return;this.open=true;this.notify();}
    catch(error){if(lifecycle===this.lifecycle)this.failure(error);}
  }
  retry(localId?:string){
    if(!this.active)return;
    this.error='';this.blocked=false;
    // 認証切れで破棄した結果は、明示操作でのみ残っている写真から作り直す。
    const fresh=this.store.photos.filter(x=>this.excluded.delete(x.localId));
    for(const photo of fresh)this.entries.push({identity:this.identity(photo),photo,state:'queued',text:'',error:''});
    for(const entry of this.entries)if((!localId||entry.photo.localId===localId)&&['error','empty','paused'].includes(entry.state)){entry.identity.attemptId++;entry.state='queued';entry.error='';entry.text='';}
    this.notify();void this.pump();
  }
  dispose(){this.pause();this.stopStore();this.entries=[];this.listeners.clear();}
}
