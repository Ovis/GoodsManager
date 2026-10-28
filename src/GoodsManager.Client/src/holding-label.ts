/** 画面内の並び番号で個体・ロットを識別し、内部IDは表示しない。 */
export function holdingLabel(kind:string,rows:readonly {id:string}[],id:string){
  const index=rows.findIndex(row=>row.id===id);
  return index<0?`選択した${kind}`:`${kind} ${index+1}`;
}
