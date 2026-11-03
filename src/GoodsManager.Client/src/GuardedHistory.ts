export type HistoryEntry={url:string;state:Record<string,unknown>};
export type NavigationGuard={blocks:(url:string)=>boolean;approve:()=>Promise<boolean>};
type HistoryPort={read:()=>HistoryEntry;replace:(entry:HistoryEntry)=>void;push:(entry:HistoryEntry)=>void;go:(delta:number)=>void;changed:()=>void;restoreScroll:(value:number)=>void};
const positionKey='goodsNavigationIndex';
const position=(entry:HistoryEntry)=>Number.isInteger(entry.state[positionKey])?entry.state[positionKey] as number:null;

/** popstateを閲覧画面へ反映する前に、元の履歴位置で編集の終了を確認する。 */
export class GuardedHistory{
 private active:HistoryEntry;private guard:NavigationGuard|null=null;private epoch=0;private pending=false;
 private restoring:{promise:Promise<void>;resolve:()=>void}|null=null;private replay:HistoryEntry|null=null;
 constructor(private port:HistoryPort){
  const entry=port.read();this.active={...entry,state:{...entry.state,[positionKey]:position(entry)??0}};port.replace(this.active);
 }
 register(guard:NavigationGuard){this.guard=guard;return()=>{if(this.guard===guard){this.guard=null;this.epoch++;this.pending=false;}};}
 rememberScroll(scroll:number){
  if(this.restoring||this.replay||this.port.read().url!==this.active.url)return;
  this.active={...this.active,state:{...this.port.read().state,scroll}};this.port.replace(this.active);
 }
 private blocked(url:string){return this.guard?.blocks(url)?this.guard:null;}
 private async approved(guard:NavigationGuard|null,token:number){
  let allowed=true;try{if(guard)allowed=await guard.approve();}catch{allowed=false;}
  if(this.restoring)await this.restoring.promise;
  return token===this.epoch&&allowed;
 }
 private commit(url:string,state:Record<string,unknown>,replace:boolean){
  const entry={url,state:{...state,[positionKey]:(position(this.active)??0)+(replace?0:1)}};
  if(replace)this.port.replace(entry);else this.port.push(entry);
  this.active=entry;this.port.changed();
 }
 async navigate(url:string,state:Record<string,unknown>,replace=false,force=false){
  if((this.pending||this.replay)&&!force)return;
  const token=++this.epoch,guard=force?null:this.blocked(url);
  if(!guard&&!this.restoring){this.pending=false;this.commit(url,state,replace);return;}
  this.pending=true;
  if(force&&this.restoring)await this.restoring.promise;
  const allowed=await this.approved(guard,token);
  if(token!==this.epoch)return;
  this.pending=false;if(!allowed)return;
  this.commit(url,state,replace);
 }
 private restore(from:HistoryEntry){
  let resolve!:()=>void;const promise=new Promise<void>(done=>resolve=done);
  this.restoring={promise,resolve};this.port.go(position(this.active)!-position(from)!);return promise;
 }
 async pop(){
  const target=this.port.read();
  if(this.restoring){
   if(position(target)!==position(this.active)){this.port.go(position(this.active)!-position(target)!);return;}
   const restoring=this.restoring;this.restoring=null;this.port.restoreScroll(Number(this.active.state.scroll)||0);restoring.resolve();return;
  }
  if(this.replay){
   const expected=this.replay;this.replay=null;this.pending=false;
   if(position(target)===position(expected)){this.active=target;this.port.changed();return;}
  }
  const guard=this.blocked(target.url),delta=(position(target)??0)-(position(this.active)??0);
  // 同じページのアンカーや、このアプリが作っていない履歴は通常の履歴処理へ委ねる。
  if(position(target)===null||delta===0){this.active=target;this.port.changed();return;}
  if(this.pending){await this.restore(target);return;}
  if(!guard){this.active=target;this.port.changed();return;}
  this.pending=true;const token=++this.epoch;await this.restore(target);
  const allowed=await this.approved(guard,token);if(token!==this.epoch)return;
  if(!allowed){this.pending=false;return;}
  this.replay=target;this.port.go(delta);
 }
}
