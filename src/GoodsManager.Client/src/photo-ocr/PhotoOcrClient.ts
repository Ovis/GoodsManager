import {ApiError,type ApiClient} from '../api';

export type OcrIdentity={accountId:string;collectionId:string;draftEpoch:number;localId:string;attemptId:number};
export type OcrResult={text:string;preparationMs?:number;normalizationMs?:number;recognitionMs?:number};
export class PhotoOcrError extends Error {
  constructor(public stage:'prepare'|'normalize'|'recognize'|'cancelled'|'access',message:string){super(message);}
}
type Access={available:boolean;assetBase:string};
type Pending={id:number;identity:string;resolve:(value:any)=>void;reject:(error:unknown)=>void;timer:ReturnType<typeof setTimeout>};
export interface OcrClient {
  authorize():Promise<void>;
  recognize(file:File,identity:OcrIdentity,progress:(state:'preparing'|'recognizing')=>void):Promise<OcrResult>;
  cancel():void;
}

/** 1Workerで正規化と認識を直列実行。破棄時には待機中Promiseも必ず確定する。 */
export class PhotoOcrClient implements OcrClient {
  private worker:Worker|null=null;private pending:Pending|null=null;private sequence=0;private generation=0;private base='';
  private accessChecks=new Set<AbortController>();
  constructor(private request:ApiClient,private createWorker=(url:string)=>new Worker(url,{type:'module'}),private limits={normalize:60000,prepare:180000,recognize:180000}){}
  async authorize(){
    const generation=this.generation,controller=new AbortController();this.accessChecks.add(controller);
    const timeout=setTimeout(()=>controller.abort(),this.limits.prepare);
    const aborted=new Promise<never>((_,reject)=>controller.signal.addEventListener('abort',()=>reject(new PhotoOcrError(generation!==this.generation?'cancelled':'prepare','文字読み取りの利用確認を停止しました。再試行できます。')),{once:true}));
    try{
      const access=await Promise.race([this.request<Access>('/registration/photo-ocr',{signal:controller.signal}),aborted]);
      if(!access.available)throw new PhotoOcrError('prepare','文字読み取りを準備できません。写真の登録は続けられます。');
      this.base=access.assetBase;
    }catch(error){
      if(error instanceof ApiError&&(error.status===401||error.status===403||error.code==='ACCOUNT_CHANGED'||error.code==='COLLECTION_NOT_FOUND'))throw new PhotoOcrError('access','ログインまたはコレクションの利用権限が変わりました。');
      if(error instanceof PhotoOcrError)throw error;
      throw new PhotoOcrError('prepare','文字読み取りの利用確認に失敗しました。再試行できます。');
    }finally{clearTimeout(timeout);this.accessChecks.delete(controller);}
  }
  cancel(){
    ++this.generation;this.worker?.terminate();this.worker=null;
    for(const controller of this.accessChecks)controller.abort();this.accessChecks.clear();
    const pending=this.pending;this.pending=null;
    if(pending){clearTimeout(pending.timer);pending.reject(new PhotoOcrError('cancelled','文字読み取りを停止しました。'));}
  }
  private exchange(action:'prepare'|'normalize'|'recognize',identity:OcrIdentity,body:Record<string,unknown>={}){
    const worker=this.worker;const id=++this.sequence;
    if(!worker)return Promise.reject(new PhotoOcrError('cancelled','文字読み取りを停止しました。'));
    return new Promise<any>((resolve,reject)=>{
      const fail=()=>{if(this.pending?.id!==id)return;const waiting=this.pending;this.pending=null;clearTimeout(waiting.timer);this.worker?.terminate();this.worker=null;waiting.reject(new PhotoOcrError(action,'文字読み取りに失敗しました。再試行するか、登録を続けてください。'));};
      const timer=setTimeout(fail,this.limits[action]);
      this.pending={id,identity:JSON.stringify(identity),resolve,reject,timer};
      worker.onerror=fail;worker.onmessageerror=fail;
      worker.onmessage=({data})=>{
        if(this.pending?.id!==id||data.requestId!==id||JSON.stringify(data.identity)!==this.pending.identity)return;
        if(data.error){fail();return;}
        if(!data.done)return;
        const waiting=this.pending;this.pending=null;clearTimeout(waiting.timer);waiting.resolve(data);
      };
      try{worker.postMessage({requestId:id,identity,action,root:this.base,...body});}catch{fail();}
    });
  }
  async recognize(file:File,identity:OcrIdentity,progress:(state:'preparing'|'recognizing')=>void){
    const generation=this.generation;
    await this.authorize();
    if(generation!==this.generation)throw new PhotoOcrError('cancelled','文字読み取りを停止しました。');
    let preparationMs=0;
    if(!this.worker){
      progress('preparing');
      try{this.worker=this.createWorker(this.base+'workers/ocr.worker.js');}catch{throw new PhotoOcrError('prepare','このブラウザでは文字読み取りを準備できません。');}
      preparationMs=(await this.exchange('prepare',identity)).preparationMs;
    }
    progress('recognizing');
    const normalized=await this.exchange('normalize',identity,{file});
    try{
      const result=await this.exchange('recognize',identity,{blob:normalized.blob});
      await this.authorize();
      if(generation!==this.generation)throw new PhotoOcrError('cancelled','文字読み取りを停止しました。');
      return {text:typeof result.text==='string'?result.text.trim():'',preparationMs,normalizationMs:normalized.normalizationMs,recognitionMs:result.recognitionMs};
    }finally{normalized.blob=null;}
  }
}
