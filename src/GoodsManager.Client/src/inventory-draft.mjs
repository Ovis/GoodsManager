import {validText} from './registration-state.mjs';
export function fields(row){return {condition:row.condition||null,storageLocation:row.storageLocation||null,purchasePlace:row.purchasePlace||null,memo:row.memo||null};}
export function checkFields(row){for(const key of ['storageLocation','purchasePlace','memo'])if(!validText(row[key]||'',key==='memo'?4000:200))throw new Error(`${key==='memo'?'メモ':key==='storageLocation'?'保管場所':'購入先'}の文字数・使用できる文字を確認してください。`);if(![null,'','unopened','opened','inUse'].includes(row.condition))throw new Error('状態を確認してください。');}
export function quantity(value,minimum=0){if(!/^\d+$/.test(String(value))||!Number.isInteger(Number(value))||Number(value)<minimum||Number(value)>2147483647)throw new Error(`数量は${minimum}〜2,147,483,647の整数で入力してください。`);return Number(value);}
export function total(state){const value=state.lots.reduce((n,x)=>n+x.quantity,0)+state.items.length;if(!Number.isSafeInteger(value))throw new Error('合計数量が安全な整数の上限を超えています。');return value;}
/** 操作をコピーへ適用し、失敗時は元の仮編集状態も変更しない */
export function applyInventory(state,command){
 const next={lots:state.lots.map(x=>({...x})),items:state.items.map(x=>({...x,images:[...(x.images||[])]}))};
 const lot=()=>{const r=next.lots.find(x=>x.id===command.lotRef);if(!r)throw new Error('元のロットが見つかりません。');return r;};
 const item=()=>{const r=next.items.find(x=>x.id===command.itemRef);if(!r)throw new Error('元の個体が見つかりません。');return r;};
 const unique=id=>{if(!id||next.lots.some(x=>x.id===id)||next.items.some(x=>x.id===id))throw new Error('操作の参照が重複しています。');};
 const before=total(next);let delta=0;
 switch(command.type){
  case 'createLot':{unique(command.clientRef);const q=quantity(command.quantity,1);checkFields(command.fields);next.lots.push({id:command.clientRef,quantity:q,...fields(command.fields)});delta=q;break;}
  case 'updateLot':{const r=lot();if(command.quantity!==undefined){const q=quantity(command.quantity);delta=q-r.quantity;r.quantity=q;}checkFields({...r,...command.fields});Object.assign(r,command.fields);if(r.quantity===0)next.lots=next.lots.filter(x=>x!==r);break;}
  case 'splitLot':{const r=lot(),q=quantity(command.quantity,1);if(q>=r.quantity)throw new Error('分割数は元の数量より少なくしてください。');unique(command.newLotRef);const copy={...r,...command.fields,id:command.newLotRef,quantity:q};checkFields(copy);r.quantity-=q;next.lots.push(copy);break;}
  case 'extractItems':{const r=lot();if(command.itemRefs.length<1||command.itemRefs.length>r.quantity||command.itemRefs.length>100)throw new Error('切り出す個数を確認してください。');if(command.fields?.memo!==undefined)throw new Error('個体化ではメモを引き継ぎません。');for(const id of command.itemRefs){unique(id);const value={...fields(r),...command.fields,id,memo:null,images:[]};checkFields(value);next.items.push(value);}r.quantity-=command.itemRefs.length;if(!r.quantity)next.lots=next.lots.filter(x=>x!==r);break;}
  case 'updateItem':{const r=item();checkFields({...r,...command.fields});Object.assign(r,command.fields);break;}
  case 'returnItem':{const r=item();if(command.memoAction!=='discardConfirmed'||!['moveToProduct','deleteConfirmed'].includes(command.photoAction))throw new Error('固有メモと写真の扱いを確認してください。');unique(command.newLotRef);next.lots.push({id:command.newLotRef,quantity:1,...fields(r),memo:null});next.items=next.items.filter(x=>x!==r);break;}
  case 'replaceItemImages':item();break;
  default:throw new Error('対応していない所持操作です。');
 }
 if(total(next)!==before+delta)throw new Error('数量の整合性を確認できませんでした。');return next;
}
export function appendCommands(state,existing,added){const commands=[...existing,...added];if(commands.length>100)throw new Error('一度の保存は100操作までです。先に変更を保存してください。');if(commands.filter(x=>x.type==='extractItems').reduce((n,x)=>n+x.itemRefs.length,0)>100)throw new Error('一度の保存で切り出せる個体は合計100件までです。');let next=state;for(const command of added)next=applyInventory(next,command);return {state:next,commands};}
