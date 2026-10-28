import {api,type ApiClient} from './api';
import {RegistrationStore,type Photo} from './RegistrationStore';
import type {ImageRef} from './types';
export type ImageEntry={ref:string;original?:ImageRef;photo?:Photo};
/** 保存済みの関連と先行アップロードを分け、正式保存まで画像リストだけを編集 */
export class ImageDraft{
 entries:ImageEntry[];primary:string|null;uploads:RegistrationStore;
 constructor(images:ImageRef[]=[],entries?:ImageEntry[],request:ApiClient=api){this.uploads=new RegistrationStore(request);this.entries=entries?entries.map(x=>({...x})):images.map(x=>({ref:x.linkId,original:x}));this.primary=images.find(x=>x.isPrimary)?.linkId??this.entries[0]?.ref??null;}
 async add(files:File[],replacement?:string){if(replacement&&files.length!==1)throw new Error('差し替えには画像を1枚選択してください。');const previous=this.entries.find(x=>x.ref===replacement);if(previous?.photo&&this.uploads.photos.includes(previous.photo)){await this.uploads.cancelPhoto(previous.photo);if(this.uploads.photos.includes(previous.photo))throw new Error(this.uploads.error||'先の送信結果を確認してください。');}const old=this.uploads.photos.length;this.uploads.addFiles(files);const photos=this.uploads.photos.slice(old);if(photos.length!==files.length)throw new Error(this.uploads.error||'画像を追加できませんでした。');if(previous)previous.photo=photos[0];else for(const photo of photos)this.entries.push({ref:'photo-'+crypto.randomUUID(),photo});this.primary??=this.entries[0]?.ref??null;this.uploads.notify();}
 async remove(ref:string){const entry=this.entries.find(x=>x.ref===ref);if(!entry)return;if(entry.photo&&this.uploads.photos.includes(entry.photo)){await this.uploads.cancelPhoto(entry.photo);if(this.uploads.photos.includes(entry.photo))throw new Error(this.uploads.error||'画像の送信結果を確認してください。');}this.entries=this.entries.filter(x=>x!==entry);if(this.primary===ref)this.primary=this.entries[0]?.ref??null;this.uploads.notify();}
 move(ref:string,delta:number){const index=this.entries.findIndex(x=>x.ref===ref);const target=index+delta;if(index<0||target<0||target>=this.entries.length)return;[this.entries[index],this.entries[target]]=[this.entries[target],this.entries[index]];this.uploads.notify();}
 serialize(){if(this.entries.some(x=>x.photo&&x.photo.state!=='ready'))throw new Error('画像の変換完了を待つか、画像ごとに再試行・取消してください。');return this.entries.map((x,sortOrder)=>x.original?{linkId:x.original.linkId,sortOrder,...(x.photo?{replacementUploadId:x.photo.id}:{})}:{clientRef:x.ref,temporaryUploadId:x.photo!.id,sortOrder});}
 preview(entry:ImageEntry){return entry.photo?.previewUrl||entry.photo?.url||entry.original?.thumbnailUrl||'';}
 restoreCancelled(photos:Photo[]){for(const photo of photos){photo.url=URL.createObjectURL(photo.file);this.uploads.photos.push(photo);}this.uploads.notify();}
 async cleanup(consumed:Set<string>=new Set()){
  const cancelled:Photo[]=[];
  try{for(const photo of [...this.uploads.photos]){
    if(photo.id&&consumed.has(photo.id)){++photo.generation;photo.state='consumed';URL.revokeObjectURL(photo.url);this.uploads.photos=this.uploads.photos.filter(x=>x!==photo);}
    else{await this.uploads.cancelPhoto(photo);if(this.uploads.photos.includes(photo))throw new Error(this.uploads.error||'未使用画像の取消結果を確認してください。');photo.state='cancelled';photo.id=undefined;photo.previewUrl=undefined;cancelled.push(photo);}
  }return cancelled;}catch(error){
    // 途中まで取り消せた場合も、残りの確認が終わるまで選択ファイルを再試行可能な形で保持する。
    this.restoreCancelled(cancelled);throw error;
  }
 }
}
