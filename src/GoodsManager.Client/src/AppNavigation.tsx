import {CollectionLink} from './CollectionLink';
import {navigate} from './navigation';

const items=[
  {href:'/collection',label:'コレクション',shortLabel:'一覧',icon:'collection'},
  {href:'/register',label:'商品を登録',shortLabel:'登録',icon:'register'},
  {href:'/trash',label:'ゴミ箱',shortLabel:'ゴミ箱',icon:'trash'},
  {href:'/settings',label:'設定',shortLabel:'設定',icon:'settings'}
] as const;

function NavigationIcon({name}:{name:typeof items[number]['icon']}){
  return <svg className="navigation-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true" focusable="false">
    {name==='collection'?<><rect x="3" y="3" width="7" height="7" rx="1"/><rect x="14" y="3" width="7" height="7" rx="1"/><rect x="3" y="14" width="7" height="7" rx="1"/><rect x="14" y="14" width="7" height="7" rx="1"/></>:name==='register'?<><rect x="3" y="3" width="18" height="18" rx="4"/><path d="M12 7v10M7 12h10"/></>:name==='trash'?<><path d="M3 6h18M9 6V3h6v3M5 6l1 15h12l1-15M10 10v7M14 10v7"/></>:<><path d="m10 3-.6 2.4-2 .9L5 5.5 3 9l1.8 1.7v2.6L3 15l2 3.5 2.4-.8 2 .9L10 21h4l.6-2.4 2-.9 2.4.8 2-3.5-1.8-1.7v-2.6L21 9l-2-3.5-2.4.8-2-.9L14 3Z"/><circle cx="12" cy="12" r="3"/></>}
  </svg>;
}

export function AppNavigation({pathname,mobile=false}:{pathname:string;mobile?:boolean}){
  return <nav className={mobile?'bottom-nav':undefined} aria-label={mobile?'スマートフォンナビゲーション':'メインナビゲーション'}>{items.map(item=>{
    const active=item.href==='/settings'?['/settings','/masters','/account','/backups'].includes(pathname):item.href==='/trash'?pathname.startsWith('/trash'):pathname===item.href;
    return <CollectionLink key={item.href} href={item.href} className={active?'active':undefined} aria-current={active?'page':undefined} onClick={event=>{if(event.ctrlKey||event.metaKey||event.shiftKey||event.altKey)return;event.preventDefault();navigate(item.href);}}><span className="navigation-symbol"><NavigationIcon name={item.icon}/></span><span className="navigation-label">{mobile?item.shortLabel:item.label}</span></CollectionLink>;
  })}</nav>;
}
