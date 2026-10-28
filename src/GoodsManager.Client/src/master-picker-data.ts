import {api,ApiError,type ApiClient} from './api';
import {categoryPaths,type CategoryNode} from './category-paths';
import type {NamedRef} from './types';

type PickerPage={items:NamedRef[];totalPages:number};
const categoryNames=(signal:AbortSignal,request:ApiClient)=>request<{nodes:CategoryNode[]}>('/categories/tree',{signal}).then(result=>categoryPaths(result.nodes));

export async function loadPickerPage(kind:string,params:URLSearchParams,signal:AbortSignal,request:ApiClient=api){
  const [result,paths]=await Promise.all([
    request<PickerPage>(`/masters/${kind}?${params}`,{signal}),
    kind==='categories'?categoryNames(signal,request):Promise.resolve(null)
  ]);
  const names=paths??Object.fromEntries(result.items.map(item=>[item.id,item.name]));
  return {page:{...result,items:result.items.map(item=>({...item,name:names[item.id]||item.name}))},names};
}

export async function loadPickerNames(kind:string,ids:string[],signal:AbortSignal,request:ApiClient=api):Promise<Record<string,string>>{
  if(!ids.length)return {};
  if(kind==='categories'){const names=await categoryNames(signal,request);for(const id of ids)if(!names[id])names[id]='削除済みのカテゴリ';return names;}
  const items=await Promise.all(ids.map(id=>request<NamedRef>(`/masters/${kind}/${encodeURIComponent(id)}`,{signal}).catch(error=>{if(error instanceof ApiError&&error.status===404)return {id,name:'削除済みの項目'};throw error;})));
  return Object.fromEntries(items.map(item=>[item.id,item.name]));
}
