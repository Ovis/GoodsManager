import {useEffect,useId,useRef,useState} from 'react';
import type {CollectionSpace} from './CollectionsStore';

export function CollectionSwitcher({items,value,onChange,className=''}:{items:CollectionSpace[];value:string;onChange:(id:string)=>void;className?:string}){
  const [open,setOpen]=useState(false),id=useId(),root=useRef<HTMLDivElement>(null),trigger=useRef<HTMLButtonElement>(null),panel=useRef<HTMLDivElement>(null);
  const current=items.find(item=>item.id===value)?.name||'選択してください';
  useEffect(()=>{
    if(!open)return;
    const outside=(event:PointerEvent)=>{if(!root.current?.contains(event.target as Node))setOpen(false);};
    const escape=(event:KeyboardEvent)=>{if(event.key==='Escape'){event.preventDefault();setOpen(false);trigger.current?.focus({preventScroll:true});}};
    const resize=()=>setOpen(false);
    document.addEventListener('pointerdown',outside);document.addEventListener('keydown',escape);window.addEventListener('resize',resize);
    (panel.current?.querySelector<HTMLButtonElement>('[aria-pressed="true"]')||panel.current?.querySelector<HTMLButtonElement>('button'))?.focus({preventScroll:true});
    return()=>{document.removeEventListener('pointerdown',outside);document.removeEventListener('keydown',escape);window.removeEventListener('resize',resize);};
  },[open]);
  const choose=(item:CollectionSpace)=>{setOpen(false);trigger.current?.focus({preventScroll:true});if(item.id!==value)onChange(item.id);};
  return <div ref={root} className={`collection-switch ${className}`} onBlur={event=>{if(!event.currentTarget.contains(event.relatedTarget))setOpen(false);}}>
    <button ref={trigger} type="button" className="collection-current" aria-label={`コレクションを切り替え：${current}`} aria-expanded={open} aria-controls={open?id:undefined} title={current} disabled={!items.length} onClick={()=>setOpen(!open)}>
      <span className="collection-mark" aria-hidden="true"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7"><rect x="3" y="3" width="7" height="7" rx="1"/><rect x="14" y="3" width="7" height="7" rx="1"/><rect x="3" y="14" width="7" height="7" rx="1"/><rect x="14" y="14" width="7" height="7" rx="1"/></svg></span>
      <span className="collection-current-text"><span className="collection-caption">コレクション</span><strong>{current}</strong></span>
      <svg className="collection-chevron" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="m8 10 4 4 4-4"/></svg>
    </button>
    {open&&<div ref={panel} id={id} className="collection-choice-panel" role="group" aria-label="コレクション一覧"><p className="collection-choice-title">コレクションを切り替え</p><ul>{items.map(item=><li key={item.id}><button type="button" className="collection-option" aria-pressed={item.id===value} onClick={()=>choose(item)}><span>{item.name}</span>{item.id===value&&<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="m5 12 4 4L19 6"/></svg>}</button></li>)}</ul></div>}
  </div>;
}
