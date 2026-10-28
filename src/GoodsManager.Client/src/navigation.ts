import { useEffect, useState } from 'react';
export type Location = { pathname:string; search:string; key:string; scroll:number; draftQ?:string; returnUrl?:string };
function current(): Location { return {pathname:location.pathname,search:location.search,key:history.state?.key||'initial',scroll:history.state?.scroll||0,draftQ:history.state?.draftQ,returnUrl:history.state?.returnUrl}; }
export function collectionUrl(url:string,collectionId?:string|null){
  if(!collectionId||!/^\/(?:collection|collections|register|masters|trash|products|backups|account|login|setup)(?:[/?]|$)/.test(url))return url;
  const target=new URL(url,'https://local.invalid');target.searchParams.set('collectionId',collectionId);return target.pathname+target.search+target.hash;
}
export function navigate(url:string, options:{replace?:boolean;state?:Partial<Location>;collectionId?:string}={}) {
  url=collectionUrl(url,options.collectionId??new URLSearchParams(location.search).get('collectionId'));
  history.replaceState({...history.state,scroll:window.scrollY},'');
  const state={key:crypto.randomUUID(),scroll:0,...options.state};
  if(options.replace)history.replaceState(state,'',url); else history.pushState(state,'',url);
  window.dispatchEvent(new Event('app:navigate'));
}
export function navigateFor(collectionId:string|undefined,url:string,options:Parameters<typeof navigate>[1]={}){
  if(collectionId&&new URLSearchParams(location.search).get('collectionId')!==collectionId)return;
  navigate(url,{...options,collectionId});
}
export function useLocation() {
  const [value,setValue]=useState(current);
  useEffect(()=>{
    history.scrollRestoration='manual'; if(!history.state?.key)history.replaceState({key:crypto.randomUUID(),scroll:window.scrollY},'');
    const change=()=>setValue(current()); const scroll=()=>history.replaceState({...history.state,scroll:window.scrollY},'');
    addEventListener('popstate',change);addEventListener('app:navigate',change);addEventListener('scroll',scroll,{passive:true});
    return()=>{removeEventListener('popstate',change);removeEventListener('app:navigate',change);removeEventListener('scroll',scroll);};
  },[]);
  return value;
}
