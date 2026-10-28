import {api,ApiError,errorText,type ApiClient} from './api';
import {RegistrationStore,mutation,type Confirmation,type DuplicateResult} from './RegistrationStore';
import {ImageDraft} from './ImageDraft';
import {blankDraft,productInput} from './registration-state.mjs';
import {appendCommands,fields} from './inventory-draft.mjs';
import type {ProductDetail,Holding} from './types';
export type InventoryState={lots:(Holding&{quantity:number})[];items:Holding[]};
type Command={type:string;[key:string]:unknown};
type Area='product'|'images'|'inventory';
type Attempt={area:Area;body:unknown;etag:string;used:string[]};
export class ProductWorkspace{
 data:ProductDetail|null=null;mode:'view'|Area='view';editor=new RegistrationStore();images:ImageDraft|null=null;
 inventory:InventoryState={lots:[],items:[]};commands:Command[]=[];itemImages=new Map<string,ImageDraft>();resources=new Set<ImageDraft>();
 lotDrafts=new Map<string,{fields:Holding;quantity:string;amount:string}>();previousItemImages=new Map<string,ImageDraft>();previousCommands:Command[]=[];itemView:Holding|null=null;
 itemEdit:{id:string;fields:Holding;images:ImageDraft}|null=null;previousInventory:InventoryState|null=null;
 baseEtag='';busy=false;loading=false;error='';notice='';save=mutation<unknown>();attempt:Attempt|null=null;confirmation:Confirmation|null=null;duplicateDialog:DuplicateResult|null=null;
 conflict:{area:Area;latest:ProductDetail|null}|null=null;
 private itemVersion=0;private listeners=new Set<()=>void>();private version=0;
 constructor(public id:string,public request:ApiClient=api){this.editor=new RegistrationStore(request);this.save=mutation<unknown>(request);this.editor.excludeProductId=id;this.editor.subscribe(()=>this.notify());}
 subscribe=(listener:()=>void)=>{this.listeners.add(listener);return()=>{this.listeners.delete(listener);};};snapshot=()=>this.version;notify=()=>{++this.version;for(const listener of this.listeners)listener();};
 get locked(){return this.busy||this.save.state==='unknown'||!!this.conflict;}
 get dirty(){return this.mode!=='view'||this.save.state==='unknown'||this.resources.size>0;}
 etag(p:ProductDetail){return `"product:${p.id}:${p.revision}"`;}
 async load(force=false){if(this.loading||this.dirty||this.data&&!force)return;this.loading=true;this.error='';this.notify();try{this.data=await this.request<ProductDetail>(`/products/${this.id}`);}catch(e){this.fail(e);}finally{this.loading=false;this.notify();}}
 private attach(session:ImageDraft){this.resources.add(session);session.uploads.subscribe(()=>this.notify());return session;}
 begin(area:Area){if(!this.data||this.dirty)return;this.mode=area;this.baseEtag=this.etag(this.data);this.error='';this.notice='';this.confirmation=null;this.conflict=null;
  if(area==='product'){const p=this.data;this.editor.draft={...blankDraft(),name:p.name,categoryId:p.category.id,workIds:p.works.map(x=>x.id),characterIds:p.characters.map(x=>x.id),tagIds:p.tags.map(x=>x.id),manufacturerId:p.manufacturer?.id||'',sellerId:p.seller?.id||'',version:p.version||'',memo:p.memo||'',identifiers:p.identifiers.length?p.identifiers.map(x=>({scheme:x.scheme,value:x.rawValue})):blankDraft().identifiers,links:p.links.length?p.links.map(x=>x.url):['']};this.editor.resolved(Object.fromEntries([p.category,...p.works,...p.characters,...p.tags,...[p.manufacturer,p.seller].filter(x=>x!==null)].map(x=>[x!.id,x!.name])));this.editor.error='';this.editor.fieldErrors={};this.editor.duplicates=null;}
  if(area==='images')this.images=this.attach(new ImageDraft(this.data.images,undefined,this.request));
  if(area==='inventory'){this.inventory=structuredClone(this.data.inventory) as InventoryState;this.commands=[];this.itemImages.clear();this.lotDrafts.clear();this.previousInventory=null;this.previousItemImages.clear();this.previousCommands=[];}
  this.notify();
 }
 async open(area:Area){if(this.dirty||this.busy||this.loading)return;this.busy=true;this.notify();try{this.data=await this.request<ProductDetail>(`/products/${this.id}`);this.busy=false;this.begin(area);}catch(e){this.fail(e);}finally{this.busy=false;this.notify();}}
 async loadItem(id:string){const version=++this.itemVersion;this.itemView=null;this.notify();try{const result=await this.request<{item:Holding}>(`/products/${this.id}/items/${id}`);if(version===this.itemVersion)this.itemView=result.item;}catch(e){if(version===this.itemVersion)this.fail(e);}finally{if(version===this.itemVersion)this.notify();}} command(...added:Command[]){if(this.locked)return false;try{if(this.commands.length+added.length+this.itemImages.size>100)throw new Error('画像編集を含めて100操作までです。先に保存してください。');const result=appendCommands(this.inventory,this.commands,added);this.inventory=result.state;this.commands=result.commands;this.error='';this.notify();return true;}catch(e){this.fail(e);this.notify();return false;}}
 beginItem(id:string){if(this.locked)return;const row=this.inventory.items.find(x=>x.id===id);if(!row)return;const current=this.itemImages.get(id);const images=this.attach(new ImageDraft(row.images||[],current?.entries,this.request));this.itemEdit={id,fields:{...row},images};this.notify();}
 async cancelItem(){if(!this.itemEdit||this.locked)return;this.busy=true;this.notify();try{await this.itemEdit.images.cleanup();this.resources.delete(this.itemEdit.images);this.itemEdit=null;}catch(e){this.fail(e);}finally{this.busy=false;this.notify();}}
 applyItem(){if(!this.itemEdit||this.locked)return;try{const edit=this.itemEdit;edit.images.serialize();if(this.commands.length+1+this.itemImages.size+(this.itemImages.has(edit.id)?0:1)>100)throw new Error('画像編集を含めて100操作までです。先に保存してください。');const result=appendCommands(this.inventory,this.commands,[{type:'updateItem',itemRef:edit.id,fields:fields(edit.fields)}]);this.inventory=result.state;this.commands=result.commands;this.itemImages.set(edit.id,edit.images);this.itemEdit=null;this.error='';}catch(e){this.fail(e);}this.notify();}
 returnItem(id:string,photoAction:string){if(this.locked)return;try{const images=this.itemImages.get(id);const commands:Command[]=[];if(images)commands.push({type:'replaceItemImages',itemRef:id,entries:images.serialize()});commands.push({type:'returnItem',itemRef:id,newLotRef:'returned-'+crypto.randomUUID(),memoAction:'discardConfirmed',photoAction});const result=appendCommands(this.inventory,this.commands,commands);this.inventory=result.state;this.commands=result.commands;this.itemImages.delete(id);this.error='';}catch(e){this.fail(e);}this.notify();}
 private inventoryBody(){for(const row of this.inventory.lots){const draft=this.lotDrafts.get(row.id);if(draft&&(Number(draft.quantity)!==row.quantity||JSON.stringify(fields(draft.fields))!==JSON.stringify(fields(row))))throw new Error('未反映のロット入力があります。「このロットを仮編集へ反映」を押してください。');}const commands=[...this.commands];for(const [id,session] of this.itemImages)commands.push({type:'replaceItemImages',itemRef:id,entries:session.serialize()});if(commands.length<1)throw new Error('所持の変更を反映してください。');if(commands.length>100)throw new Error('一度の保存は画像編集も含めて100操作までです。');return {commands};}
 async submit(confirmation:Confirmation|null=this.confirmation){if(this.locked)return;try{
  let body:unknown;if(this.mode==='product'){if(this.editor.blocked||!this.editor.validate())return;this.editor.invalidateChecks();body={product:productInput(this.editor.draft),duplicateConfirmation:confirmation};}
  else if(this.mode==='images')body={entries:this.images!.serialize(),primaryRef:this.images!.primary};else if(this.mode==='inventory'){if(this.itemEdit)throw new Error('個体の編集を仮編集へ反映するか、キャンセルしてください。');body=this.inventoryBody();}else return;
  const serialized=JSON.stringify(body);if(new TextEncoder().encode(serialized).length>256*1024)throw new Error('一度に保存できる本文サイズを超えています。');
  const used:string[]=[];const visit=(value:unknown)=>{if(!value||typeof value!=='object')return;for(const [key,entry] of Object.entries(value)){if(['temporaryUploadId','replacementUploadId'].includes(key)&&typeof entry==='string')used.push(entry);else visit(entry);}};visit(body);if(used.length>100)throw new Error('一度の保存で新しく使える画像は合計100枚までです。');
  await this.perform({area:this.mode,body,etag:this.baseEtag,used});
 }catch(e){this.fail(e);this.notify();}}
 private lock(value:boolean){this.busy=value;this.editor.busy=value||!!this.conflict||this.save.state==='unknown';for(const session of this.resources)session.uploads.busy=this.editor.busy;this.notify();}
 private async perform(attempt:Attempt,replay=false){this.attempt=attempt;this.error='';this.lock(true);
  try{const path=`/products/${this.id}`+(attempt.area==='images'?'/images':attempt.area==='inventory'?'/inventory-commands':'');const result=replay?await this.save.replay():await this.save.begin(path,attempt.body,attempt.etag,attempt.area==='inventory'?'POST':'PUT');await this.success(attempt,result);}
  catch(e){this.fail(e);if(e instanceof ApiError&&e.code==='DUPLICATE_CONFIRMATION_REQUIRED'&&e.details)this.duplicateDialog=e.details as DuplicateResult;if(e instanceof ApiError&&e.status===412)this.conflict={area:attempt.area,latest:null};}
  finally{this.lock(false);}
 }
 async replay(){if(this.busy||!this.attempt)return;await this.perform(this.attempt,true);}
 async confirmDuplicate(){if(!this.duplicateDialog)return;const r=this.duplicateDialog;this.confirmation={inputFingerprint:r.inputFingerprint,confirmedCandidates:r.candidates.filter(x=>x.strength==='strong').map(x=>({productId:x.product.id,reasons:x.reasons})),decision:'update',targetProductId:this.id};this.duplicateDialog=null;await this.submit();}
 private async success(attempt:Attempt,result:unknown){this.notice='変更を保存しました。'+(typeof result==='object'&&result!==null&&'deduplicatedCount' in result&&Number(result.deduplicatedCount)>0?'同じ画像は1つにまとめました。':'');this.mode='view';this.itemEdit=null;this.conflict=null;this.duplicateDialog=null;this.attempt=null;this.confirmation=null;this.editor.invalidateChecks();
  if(attempt.area==='product')this.data=result as ProductDetail;
  else{try{this.data=await this.request<ProductDetail>(`/products/${this.id}`);}catch(e){this.notice='変更は保存済みです。最新表示の取得に失敗しました。再取得してください。';this.fail(e);}}
  // 正式保存後の一時画像の片付けを、業務保存の失敗として扱わない。
  this.editor.busy=false;for(const session of this.resources)session.uploads.busy=false;for(const session of [...this.resources])try{await session.cleanup(new Set(attempt.used));this.resources.delete(session);}catch(e){this.notice+=' 未使用画像の取消を完了できませんでした。入力ファイルをページ内に保持しています。';this.fail(e);}this.commands=[];this.itemImages.clear();this.lotDrafts.clear();this.previousInventory=null;this.previousItemImages.clear();this.previousCommands=[];
 }
 async cancel(){if(this.locked||this.editor.blocked)return;this.busy=true;this.editor.busy=true;this.notify();const restore:(()=>void)[]=[];try{for(const session of this.resources){const cancelled=await session.cleanup();restore.push(()=>session.restoreCancelled(cancelled||[]));}this.resources.clear();this.editor.invalidateChecks();this.mode='view';this.images=null;this.itemEdit=null;this.commands=[];this.itemImages.clear();this.lotDrafts.clear();this.previousInventory=null;this.previousItemImages.clear();this.previousCommands=[];this.error='';this.notice='仮編集を取り消しました。保存内容は変わりません。';}catch(e){for(const action of restore)action();this.fail(e);}finally{this.busy=false;this.editor.busy=false;this.notify();}}
 async latest(){if(!this.conflict||this.busy)return;this.lock(true);try{this.conflict.latest=await this.request<ProductDetail>(`/products/${this.id}`);}catch(e){this.fail(e);}finally{this.lock(false);}}
 async reviewed(){if(!this.conflict?.latest)return;const {area,latest}=this.conflict;this.baseEtag=this.etag(latest);this.conflict=null;this.editor.busy=false;for(const s of this.resources)s.uploads.busy=false;
  if(area==='inventory'){this.previousInventory=this.inventory;this.previousItemImages=new Map(this.itemImages);this.previousCommands=[...this.commands];this.lotDrafts.clear();this.data=latest;this.inventory=structuredClone(latest.inventory) as InventoryState;this.commands=[];this.itemImages.clear();this.itemEdit=null;this.error='';this.notice='元の仮編集を比較用に保持しました。最新内容から所持操作を組み直してください。';this.notify();}else await this.submit();
 }
 backToReview(){this.conflict=null;this.editor.busy=false;for(const session of this.resources)session.uploads.busy=false;this.notify();}
 fail(e:unknown){if(e instanceof ApiError&&e.errors)this.editor.fieldErrors=e.errors;this.error=e instanceof ApiError?errorText(e):e instanceof Error?e.message:'処理を完了できませんでした。';}
}
