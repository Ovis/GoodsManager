import { useEffect, useState } from 'react';
import {GuardedHistory,type NavigationGuard} from './GuardedHistory';
import {collectionDisplayEntry} from './collection-display.mjs';
export type Location = { pathname:string; search:string; key:string; scroll:number; draftQ?:string; returnUrl?:string; collectionDisplayResolved?:boolean };
function currentEntry(){
  const entry={url:location.pathname+location.search+location.hash,state:history.state||{}},resolved=collectionDisplayEntry(entry);
  if(resolved!==entry)history.replaceState(resolved.state,'',resolved.url);
  return resolved;
}
function current(): Location { currentEntry();return {pathname:location.pathname,search:location.search,key:history.state?.key||'initial',scroll:history.state?.scroll||0,draftQ:history.state?.draftQ,returnUrl:history.state?.returnUrl,collectionDisplayResolved:history.state?.collectionDisplayResolved}; }
let controller:GuardedHistory|undefined;
function navigation(){
  if(!controller){
    history.scrollRestoration='manual';if(!history.state?.key)history.replaceState({...history.state,key:crypto.randomUUID(),scroll:window.scrollY},'');
    controller=new GuardedHistory({read:currentEntry,replace:entry=>history.replaceState(entry.state,'',entry.url),push:entry=>history.pushState(entry.state,'',entry.url),go:delta=>history.go(delta),changed:()=>window.dispatchEvent(new Event('app:navigate')),restoreScroll:scroll=>window.scrollTo(0,scroll)});
    addEventListener('popstate',()=>void controller!.pop());
  }
  return controller;
}
export function registerNavigationGuard(guard:NavigationGuard){return navigation().register(guard);}
export function collectionUrl(url:string,collectionId?:string|null){
  if(!collectionId||!/^\/(?:collection|collections|register|masters|trash|products|backups|account|settings|login|setup)(?:[/?]|$)/.test(url))return url;
  const target=new URL(url,'https://local.invalid');target.searchParams.set('collectionId',collectionId);return target.pathname+target.search+target.hash;
}
export function navigate(url:string, options:{replace?:boolean;state?:Partial<Location>;collectionId?:string}={}) {
  url=collectionUrl(url,options.collectionId??new URLSearchParams(location.search).get('collectionId'));
  const router=navigation();router.rememberScroll(window.scrollY);
  const state={key:crypto.randomUUID(),scroll:0,...options.state};
  const entry=collectionDisplayEntry({url,state});
  // 認証切れ・初期登録への案内では入力を保持したまま、認証画面への移動を優先する。
  void router.navigate(entry.url,entry.state,options.replace,/^\/(?:login|setup)(?:[?#]|$)/.test(url));
}
export function navigateFor(collectionId:string|undefined,url:string,options:Parameters<typeof navigate>[1]={}){
  if(collectionId&&new URLSearchParams(location.search).get('collectionId')!==collectionId)return;
  navigate(url,{...options,collectionId});
}
export function useLocation() {
  const [value,setValue]=useState(current);
  useEffect(()=>{
    const router=navigation();const change=()=>setValue(current());const scroll=()=>router.rememberScroll(window.scrollY);
    addEventListener('app:navigate',change);addEventListener('scroll',scroll,{passive:true});
    return()=>{removeEventListener('app:navigate',change);removeEventListener('scroll',scroll);};
  },[]);
  return value;
}
