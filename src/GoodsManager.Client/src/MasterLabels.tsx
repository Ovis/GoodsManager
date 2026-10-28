import {useEffect,useState} from 'react';
import {loadPickerNames} from './master-picker-data';

/** 表示用の名前だけを取得し、送信や選択には元のIDを使う。 */
export function useMasterLabels(kind:string,ids:string[],version=''){
  const key=kind+'|'+ids.join('|')+'|'+version;
  const [result,setResult]=useState<{key:string;names:Record<string,string>;failed:boolean}>({key:'',names:{},failed:false});
  const [retry,setRetry]=useState(0);
  useEffect(()=>{
    const abort=new AbortController();
    setResult({key,names:{},failed:false});
    if(ids.length)void loadPickerNames(kind,ids,abort.signal).then(names=>{
      if(!abort.signal.aborted)setResult({key,names,failed:false});
    }).catch(()=>{if(!abort.signal.aborted)setResult({key,names:{},failed:true});});
    return()=>abort.abort();
  },[key,retry]);
  const names=result.key===key?result.names:{};
  const failed=result.key===key&&result.failed;
  return {names,failed,label:(id:string)=>names[id]||(failed?'名前を取得できない項目':'名前を取得中…'),retry:()=>setRetry(n=>n+1)};
}

export function MasterNames({kind,ids,empty='なし'}:{kind:string;ids:string[];empty?:string}){
  const labels=useMasterLabels(kind,ids);
  return <>{ids.map(labels.label).join('・')||empty}{labels.failed&&<button type="button" className="btn small quiet" onClick={labels.retry}>名前を再取得</button>}</>;
}

export function ReferenceFilters({search}:{search:string}){
  const params=new URLSearchParams(search);
  const fields:Record<string,[string,string]>={categoryIds:['categories','カテゴリ'],workIds:['works','作品'],seriesIds:['series','シリーズ'],characterIds:['characters','キャラクター'],tagIds:['tags','タグ'],manufacturerIds:['organizations','製造元'],sellerIds:['organizations','販売元']};
  return <div className="applied" aria-label="適用した参照条件">{Object.entries(fields).map(([field,[kind,label]])=>{
    const ids=params.getAll(field).filter(Boolean);
    return ids.length?<span className="chip" key={field}>{label}：<MasterNames kind={kind} ids={ids}/></span>:null;
  })}</div>;
}
