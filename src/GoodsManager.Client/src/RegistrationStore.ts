import {api,ApiError,errorText} from './api';
import {FrozenMutation,type FrozenRequest} from './frozen-mutation';
import {blankDraft,nextDraft,carryKeys,codeError,duplicateInput,productInput,ownershipFields,validText,LatestCheck} from './registration-state.mjs';
import type {ProductSummary,ProductDetail,NamedRef} from './types';
export type RegistrationDraft={name:string;categoryId:string;quantity:string;workIds:string[];characterIds:string[];tagIds:string[];condition:string;storageLocation:string;purchasePlace:string;ownershipMemo:string;version:string;manufacturerId:string;sellerId:string;memo:string;identifiers:{scheme:string;value:string}[];links:string[]};
export type Candidate={product:ProductSummary;strength:'strong'|'weak';reasons:string[];isTrashed:boolean};
export type DuplicateResult={inputFingerprint:string;candidates:Candidate[];strongCandidateCount:number;weakCandidateCount:number};
export type Confirmation={inputFingerprint:string;confirmedCandidates:{productId:string;reasons:string[]}[];decision:'createSeparate'|'addToExisting'|'update'|'restore';targetProductId:string|null};
export type UploadStatus={id:string;state:string;previewUrl:string|null;errorCode:string|null;progressStage:string};
export type Photo={localId:string;file:File;url:string;key:string;state:string;progress:number;id?:string;previewUrl?:string;error?:string;xhr?:XMLHttpRequest;generation:number;sentAt?:number};
export type SaveResult={product:ProductDetail;outcome:string;deduplicatedCount:number};
export type SaveAttempt={draft:RegistrationDraft;mode:'next'|'finish';target:Candidate|null;confirmation:Confirmation|null;photoIds:string[]};
export function mutation<T>(){return new FrozenMutation<T>({csrf:async()=>(await api<{token:string}>('/auth/csrf')).token,post:async(saved:FrozenRequest,csrf:string)=>{
  const headers:Record<string,string>={'Content-Type':'application/json','X-CSRF-Token':csrf,'Idempotency-Key':saved.key,...saved.headers};if(saved.etag)headers['If-Match']=saved.etag;
  const response=await fetch('/api/v1'+saved.path,{method:saved.method,headers,body:saved.body,credentials:'same-origin',cache:'no-store'});
  if(!response.ok){const b=await response.json().catch(()=>({}));if(response.status===401)window.dispatchEvent(new Event('app:session-expired'));throw new ApiError(response.status,b.code||'HTTP_ERROR',b.traceId,b.details,b.errors);}
  return response.status===204?undefined as T:response.json() as Promise<T>;
}});}
const imageErrors:Record<string,string>={IMAGE_UNSUPPORTED_FORMAT:'JPEG・PNG・WebPを選択してください。',IMAGE_ANIMATED:'アニメーション画像は使用できません。',IMAGE_PIXEL_LIMIT:'40MP・長辺16,384pxまでの画像を選択してください。',IMAGE_SIZE_LIMIT:'1枚30MiBまでの画像を選択してください。',IMAGE_TIMEOUT:'画像の変換が30秒で完了しませんでした。',IMAGE_MEMORY_LIMIT:'画像の変換がメモリ監視値を超えました。',UPLOAD_EXPIRED:'未使用のまま24時間経過しました。再アップロードしてください。'};
/** 入力・選択ファイル・保存要求はログイン画面を挟んでも同じページのメモリに残す */
export class RegistrationStore{
  excludeProductId:string|undefined;draft:RegistrationDraft=blankDraft();carryEnabled=true;carrySelected:string[]=[...carryKeys];photos:Photo[]=[];names:Record<string,string>={};
  save=mutation<SaveResult>();masterJobs:Record<string,{create:FrozenMutation<NamedRef&{etag:string}>;relation:FrozenMutation<NamedRef&{etag:string}>;works:string[];result?:NamedRef&{etag:string}}>={};
  attempt:SaveAttempt|null=null;confirmed:Confirmation|null=null;busy=false;notice='';error='';fieldErrors:Record<string,string[]>={};
  duplicates:DuplicateResult|null=null;checking=false;checkError='';private checks=new LatestCheck();private checkTimer:ReturnType<typeof setTimeout>|null=null;private checkAbort:AbortController|null=null;
  dialog:{attempt:SaveAttempt;result:DuplicateResult;target:Candidate|null}|null=null;
  conflict:{attempt:SaveAttempt;latest:ProductDetail|null}|null=null;
  private listeners=new Set<()=>void>();private version=0;private uploadBusy=false;
  subscribe=(fn:()=>void)=>{this.listeners.add(fn);return()=>this.listeners.delete(fn);};snapshot=()=>this.version;
  notify=()=>{++this.version;for(const fn of this.listeners)fn();};
  get blocked(){return this.busy||this.save.state==='unknown'||this.save.state==='sending'||!!this.conflict||Object.values(this.masterJobs).some(x=>[x.create.state,x.relation.state].some(s=>s==='unknown'||s==='sending')||!x.result&&x.create.state==='failed'||!!x.result&&!!x.relation.last&&x.relation.state==='failed');}
  get dirty(){return JSON.stringify(this.draft)!==JSON.stringify(blankDraft())||this.photos.length>0||this.save.state==='unknown';}
  update<K extends keyof RegistrationDraft>(field:K,value:RegistrationDraft[K]){if(this.blocked)return;this.draft={...this.draft,[field]:value};this.error='';this.fieldErrors={};this.notify();if(['name','workIds','identifiers','categoryId','characterIds'].includes(field))this.scheduleCheck();}
  resolved(names:Record<string,string>){this.names={...this.names,...names};}
  invalidateChecks(){this.checks.invalidate();if(this.checkTimer)clearTimeout(this.checkTimer);this.checkTimer=null;this.checkAbort?.abort();this.checking=false;}
  scheduleCheck(){this.invalidateChecks();const version=this.checks.version;this.checkError='';this.duplicates=null;
    if(!this.draft.name.trim()&&!this.draft.identifiers.some(x=>x.value)){this.duplicates=null;this.notify();return;}
    if(this.draft.identifiers.some(x=>codeError(x.scheme,x.value))){this.duplicates=null;this.notify();return;}
    this.checking=true;this.notify();this.checkTimer=setTimeout(async()=>{const abort=new AbortController();this.checkAbort=abort;
      try{const result=await api<DuplicateResult>('/duplicate-checks',{method:'POST',body:JSON.stringify({input:duplicateInput(this.draft),excludeProductId:this.excludeProductId}),signal:abort.signal});if(version===this.checks.version){this.duplicates=result;this.checkError='';}}
      catch(e){if(!abort.signal.aborted&&version===this.checks.version)this.checkError=e instanceof Error?e.message:'候補を確認できませんでした。';}
      finally{if(version===this.checks.version){this.checking=false;this.notify();}}
    },300);
  }
  validate(){const errors:Record<string,string[]>={};const text=(field:string,value:string,max:number)=>{if(!validText(value,max))errors[field]=[`${max}コードポイント以内で入力してください。`];};
    text('name',this.draft.name,200);if(!this.draft.name.trim())errors.name=['商品名を入力してください。'];if(!this.draft.categoryId)errors.categoryId=['カテゴリを選択してください。'];
    if(!/^\d+$/.test(this.draft.quantity)||Number(this.draft.quantity)<1||Number(this.draft.quantity)>2147483647)errors.quantity=['数量は1〜2,147,483,647の整数で入力してください。'];
    for(const key of ['storageLocation','purchasePlace','version'] as const)text(key,this.draft[key],200);for(const key of ['memo','ownershipMemo'] as const)text(key,this.draft[key],4000);
    this.draft.identifiers.forEach((x,i)=>{text('identifiers',x.value,200);const e=codeError(x.scheme,x.value);if(e)errors[`code-${i}`]=[e];});
    this.draft.links.forEach((x,i)=>{if(!x)return;text('links',x,2048);try{const url=new URL(x);if(!['http:','https:'].includes(url.protocol)||/[\s\\\x00-\x1f]/u.test(x))throw new Error();}catch{errors[`link-${i}`]=['HTTPまたはHTTPSのURLを入力してください。'];}});
    if(this.photos.some(x=>x.state!=='ready'))errors.images=['選択したすべての画像の変換完了を待つか、失敗した画像を再試行・取消してください。'];
    this.fieldErrors=errors;this.error=Object.keys(errors).length?'入力内容を確認してください。':'';this.notify();return Object.keys(errors).length===0;
  }
  capture(mode:'next'|'finish',target:Candidate|null=null):SaveAttempt{return {draft:structuredClone(this.draft),mode,target,confirmation:this.confirmed,photoIds:this.photos.map(x=>x.id!)};}
  confirmation(result:DuplicateResult,target:Candidate|null):Confirmation{return {inputFingerprint:result.inputFingerprint,confirmedCandidates:result.candidates.filter(x=>x.strength==='strong').map(x=>({productId:x.product.id,reasons:[...x.reasons]})),decision:target?'addToExisting':'createSeparate',targetProductId:target?.product.id??null};}
  async submit(mode:'next'|'finish',onSuccess:(result:SaveResult,mode:'next'|'finish')=>void){if(this.blocked||!this.validate())return;this.invalidateChecks();await this.perform(this.capture(mode),onSuccess);}
  async chooseExisting(candidate:Candidate,mode:'next'|'finish'){if(this.blocked||candidate.isTrashed||!this.validate())return;this.invalidateChecks();this.busy=true;this.error='';this.notify();
    try{const attempt=this.capture(mode,candidate);const result=await api<DuplicateResult>('/duplicate-checks',{method:'POST',body:JSON.stringify({input:duplicateInput(attempt.draft)})});const current=result.candidates.find(x=>x.product.id===candidate.product.id);this.dialog={attempt,result,target:current??candidate};}
    catch(e){this.showError(e);}finally{this.busy=false;this.notify();}
  }
  async confirm(target:Candidate|null,onSuccess:(result:SaveResult,mode:'next'|'finish')=>void){if(!this.dialog)return;const {attempt,result}=this.dialog;this.confirmed=this.confirmation(result,target);this.dialog=null;await this.perform({...attempt,target,confirmation:this.confirmed},onSuccess);}
  async perform(attempt:SaveAttempt,onSuccess:(result:SaveResult,mode:'next'|'finish')=>void){if(this.blocked)return;this.attempt=attempt;this.busy=true;this.error='';this.notice='';this.notify();
    const lot={quantity:Number(attempt.draft.quantity),fields:ownershipFields(attempt.draft)};const images=attempt.photoIds.map(id=>({temporaryUploadId:id}));
    try{const result=attempt.target?await this.save.begin(`/products/${attempt.target.product.id}/holdings`,{input:duplicateInput(attempt.draft),lot,images,duplicateConfirmation:attempt.confirmation},`"product:${attempt.target.product.id}:${attempt.target.product.revision}"`):await this.save.begin('/products',{product:productInput(attempt.draft),initialLot:lot,images,duplicateConfirmation:attempt.confirmation});this.success(result,attempt,onSuccess);}
    catch(e){await this.failed(e,attempt);}finally{this.busy=false;this.notify();}
  }
  async replay(onSuccess:(result:SaveResult,mode:'next'|'finish')=>void){if(this.busy||!this.attempt)return;this.busy=true;this.error='';this.notify();try{const result=await this.save.replay();this.success(result,this.attempt,onSuccess);}catch(e){await this.failed(e,this.attempt);}finally{this.busy=false;this.notify();}}
  private success(result:SaveResult,attempt:SaveAttempt,onSuccess:(result:SaveResult,mode:'next'|'finish')=>void){for(const photo of this.photos)URL.revokeObjectURL(photo.url);this.photos=[];this.draft=nextDraft(attempt.draft,this.carryEnabled,this.carrySelected);this.confirmed=null;this.dialog=null;this.conflict=null;this.attempt=null;this.duplicates=null;this.error='';this.fieldErrors={};this.notice=`${result.outcome==='created'?'登録しました':'既存商品に追加しました'}。${result.deduplicatedCount?'同じ画像は既存の画像を維持しました。':''}${attempt.mode==='next'?'次の商品を撮影できます。':''}`;this.notify();onSuccess(result,attempt.mode);}
  private async failed(error:unknown,attempt:SaveAttempt){this.showError(error);if(error instanceof ApiError&&error.code==='DUPLICATE_CONFIRMATION_REQUIRED'&&error.details){const result=error.details as DuplicateResult;if(typeof result.inputFingerprint==='string'&&Array.isArray(result.candidates))this.dialog={attempt,result,target:attempt.target};}
    if(error instanceof ApiError&&error.status===412&&attempt.target)this.conflict={attempt,latest:null};
    if(error instanceof ApiError&&error.code.startsWith('UPLOAD_'))for(const photo of this.photos)if(photo.id)try{const status=await api<UploadStatus>(`/temporary-uploads/${photo.id}`);this.applyStatus(photo,status);}catch{/* 取得失敗でも選択ファイルとキーを保持 */}
    if(this.save.state==='unknown')this.error='保存結果を確認できませんでした。入力を保持しています。同じ保存要求を再送して結果を確認してください。';
  }
  showError(error:unknown){this.error=error instanceof ApiError?errorText(error):error instanceof Error?error.message:'通信を完了できませんでした。';if(error instanceof ApiError&&error.errors)this.fieldErrors=error.errors;}
  async refreshConflict(){if(!this.conflict)return;this.busy=true;this.notify();try{this.conflict.latest=await api<ProductDetail>(`/products/${this.conflict.attempt.target!.product.id}`);}catch(e){this.showError(e);}finally{this.busy=false;this.notify();}}
  async acceptConflict(onSuccess:(result:SaveResult,mode:'next'|'finish')=>void){if(!this.conflict?.latest)return;const {attempt,latest}=this.conflict;const target={...attempt.target!,product:{...attempt.target!.product,revision:latest.revision,name:latest.name,totalQuantity:latest.totalQuantity}};this.conflict=null;await this.perform({...attempt,target},onSuccess);}
  addFiles(files:File[]){if(this.blocked)return;if(this.photos.length+files.length>100){this.error='1商品に一度に選択できる画像は100枚までです。';this.notify();return;}for(const file of files)this.photos.push({localId:crypto.randomUUID(),file,url:URL.createObjectURL(file),key:crypto.randomUUID(),state:'queued',progress:0,generation:0});this.notify();void this.pump();}
  private async pump(){if(this.uploadBusy)return;const photo=this.photos.find(x=>x.state==='queued');if(!photo)return;this.uploadBusy=true;
    try{await this.sendPhoto(photo);}finally{this.uploadBusy=false;this.notify();void this.pump();}
  }
  private async sendPhoto(photo:Photo){const version=++photo.generation;photo.error=undefined;if(photo.file.size===0||photo.file.size>30*1024*1024){photo.state='failed';photo.error='画像は1枚30MiBまで、空のファイルは選択できません。';return;}photo.state='uploading';this.notify();
    let attempted=false;
    try{const csrf=await api<{token:string}>('/auth/csrf');if(version!==photo.generation)return;
      await new Promise<void>((resolve,reject)=>{const xhr=new XMLHttpRequest();photo.xhr=xhr;xhr.open('POST','/api/v1/temporary-uploads');xhr.setRequestHeader('X-CSRF-Token',csrf.token);xhr.setRequestHeader('Client-Upload-Key',photo.key);xhr.upload.onprogress=e=>{if(e.lengthComputable){photo.progress=Math.round(e.loaded/e.total*100);this.notify();}};
        xhr.onload=()=>{try{const result=JSON.parse(xhr.responseText);if(xhr.status!==202){if(xhr.status===401)window.dispatchEvent(new Event('app:session-expired'));throw new ApiError(xhr.status,result.code||'UPLOAD_FAILED',result.traceId);}this.applyStatus(photo,result as UploadStatus);void this.poll(photo,version);resolve();}catch(e){reject(e);}};
        xhr.onerror=()=>reject(new Error('画像の送信結果を確認できませんでした。'));xhr.onabort=()=>reject(new Error('送信を中断しました。結果の照合が必要です。'));xhr.onloadend=()=>{photo.xhr=undefined;};const form=new FormData();form.append('file',photo.file);attempted=true;photo.sentAt??=Date.now();xhr.send(form);
      });
    }catch(e){if(version!==photo.generation)return;photo.state=attempted&&(!(e instanceof ApiError)||e.status>=500)?'unknown':'failed';photo.error=e instanceof Error?e.message:'画像を送信できませんでした。';}finally{this.notify();}
  }
  private applyStatus(photo:Photo,status:UploadStatus){photo.id=status.id;photo.state=status.state;photo.previewUrl=status.previewUrl??undefined;photo.error=status.errorCode?(imageErrors[status.errorCode]??`画像処理に失敗しました（${status.errorCode}）`):undefined;}
  private async poll(photo:Photo,version:number){if(!this.photos.includes(photo)||version!==photo.generation)return;if(!['pending','processing'].includes(photo.state))return;
    try{const result=await api<UploadStatus>(`/temporary-uploads/${photo.id}`);if(version!==photo.generation)return;this.applyStatus(photo,result);this.notify();if(['pending','processing'].includes(photo.state))setTimeout(()=>void this.poll(photo,version),1000);}catch(e){if(version===photo.generation){photo.state='statusUnknown';photo.error=e instanceof Error?e.message:'変換状態を取得できませんでした。';this.notify();}}
  }
  async retryPhoto(photo:Photo){if(this.blocked||photo.xhr)return;this.error='';try{if(photo.state==='unknown'){if(this.uploadBusy)throw new Error('ほかの画像の送信完了を待ってください。');if(photo.sentAt&&Date.now()-photo.sentAt>=7*86400000)throw new Error('送信から7日経過しています。再送前に保存済み画像を確認してください。');this.uploadBusy=true;try{await this.sendPhoto(photo);}finally{this.uploadBusy=false;void this.pump();}return;}if(photo.state==='statusUnknown'&&photo.id){photo.state='pending';void this.poll(photo,photo.generation);return;}if(photo.id)await api(`/temporary-uploads/${photo.id}`,{method:'DELETE'});photo.id=undefined;photo.key=crypto.randomUUID();photo.sentAt=undefined;photo.state='queued';photo.progress=0;photo.previewUrl=undefined;void this.pump();}catch(e){this.showError(e);}finally{this.notify();}}
  async cancelPhoto(photo:Photo){if(this.blocked)return;if(photo.xhr){photo.xhr.abort();return;}if(photo.state==='unknown'){this.error='先に同じファイル・キーで送信結果を照合してください。';this.notify();return;}try{if(photo.id)await api(`/temporary-uploads/${photo.id}`,{method:'DELETE'});++photo.generation;this.photos=this.photos.filter(x=>x!==photo);URL.revokeObjectURL(photo.url);}catch(e){this.showError(e);}finally{this.notify();}}
}
