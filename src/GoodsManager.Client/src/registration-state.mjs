export const carryKeys=['workIds','categoryId','condition','storageLocation','purchasePlace','tagIds'];
export function blankDraft(){return {name:'',categoryId:'',quantity:'1',workIds:[],characterIds:[],tagIds:[],condition:'',storageLocation:'',purchasePlace:'',ownershipMemo:'',version:'',manufacturerId:'',sellerId:'',memo:'',identifiers:[{scheme:'ean13',value:''}],links:['']};}
export function nextDraft(draft,enabled,keys){const next=blankDraft();if(enabled)for(const key of keys)if(carryKeys.includes(key))next[key]=structuredClone(draft[key]);return next;}
export function codeError(scheme,raw){
  if(!raw)return '';
  let value=raw.replace(/[０-９]/g,x=>String.fromCharCode(x.charCodeAt(0)-0xfee0)).replace(/[ 　-]/g,'');if(scheme==='isbn10')value=value.replace(/x$/,'X');
  const length={ean13:13,ean8:8,upca:12,isbn10:10,isbn13:13}[scheme];
  if(!length||value.length!==length||!(scheme==='isbn10'?/^\d{9}[\dX]$/:/^\d+$/).test(value))return '種類・桁数・使用できる文字を確認してください。';
  if(scheme==='isbn13'&&!/^(978|979)/.test(value))return 'ISBN-13は978または979で始まる必要があります。';
  if(scheme==='isbn10'){const sum=[...value].reduce((n,x,i)=>n+(x==='X'?10:Number(x))*(10-i),0);if(sum%11!==0)return 'チェックディジットが一致しません。';}
  else{let sum=0;for(let i=value.length-1,weight=1;i>=0;i--,weight=weight===1?3:1)sum+=Number(value[i])*weight;if(sum%10!==0)return 'チェックディジットが一致しません。';}
  return '';
}
export function activeIdentifiers(draft){return draft.identifiers.filter(x=>x.value.length>0).map(x=>({...x}));}
export function duplicateInput(draft){return {name:draft.name,workIds:[...draft.workIds],identifiers:activeIdentifiers(draft),categoryId:draft.categoryId||null,characterIds:[...draft.characterIds]};}
export function productInput(draft){return {name:draft.name,categoryId:draft.categoryId,workIds:[...draft.workIds],characterIds:[...draft.characterIds],tagIds:[...draft.tagIds],version:draft.version||null,manufacturerId:draft.manufacturerId||null,sellerId:draft.sellerId||null,memo:draft.memo||null,identifiers:activeIdentifiers(draft),links:draft.links.filter(Boolean).map(url=>({url}))};}
export function ownershipFields(draft){return {condition:draft.condition||null,storageLocation:draft.storageLocation||null,purchasePlace:draft.purchasePlace||null,memo:draft.ownershipMemo||null};}
/** 新しい入力や保存が始まったら、前の照会の応答を採用しない */
export class LatestCheck {
  version=0;
  invalidate(){return ++this.version;}
  async run(input,fetcher,accept){const version=this.invalidate();const result=await fetcher(input);if(version===this.version)accept(result);return result;}
}

export function validText(value,max){return [...value].length<=max&&!value.includes('\0')&&!/[\uD800-\uDBFF](?![\uDC00-\uDFFF])|(?<![\uD800-\uDBFF])[\uDC00-\uDFFF]/u.test(value);}
