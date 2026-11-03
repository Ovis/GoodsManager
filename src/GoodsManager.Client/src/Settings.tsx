import {CollectionLink} from './CollectionLink';
import {navigate} from './navigation';

export function Settings({isAdministrator,collectionName}:{isAdministrator:boolean;collectionName?:string}){
  const items=[
    {href:'/collections',title:'コレクション管理',description:'コレクションの作成・名前変更や、分類などの設定コピーを行います。',available:true},
    {href:'/masters',title:'マスター管理',description:collectionName?`「${collectionName}」のカテゴリや作品、メーカー・販売元などを管理します。`:'コレクションを選択すると、カテゴリや作品などを管理できます。',available:!!collectionName},
    {href:'/settings/images',title:'画像設定',description:'このコレクションの原本保持・画像サイズ・品質を設定し、アップロードの上限を確認します。',available:!!collectionName},
    {href:'/account',title:'アカウント設定',description:'パスワードの変更や、パスキーの登録・管理を行います。',available:true},
    ...(isAdministrator?[{href:'/backups',title:'バックアップ',description:'すべてのコレクションのバックアップ設定・手動実行・履歴を管理します。',available:true}]:[])
  ];
  return <div className="settings-page"><header className="page-heading"><div><span className="eyebrow">SETTINGS</span><h1>設定</h1><p className="muted">コレクションの管理や、アカウント・アプリの設定を行います。</p></div></header><div className="settings-grid">{items.map(item=>{
    const content=<><div><h2>{item.title}</h2><p>{item.description}</p></div><span className="settings-arrow" aria-hidden="true">→</span></>;
    return item.available?<CollectionLink className="panel settings-option" key={item.href} href={item.href} onClick={event=>{if(event.ctrlKey||event.metaKey||event.shiftKey||event.altKey)return;event.preventDefault();navigate(item.href);}}>{content}</CollectionLink>:<div className="panel settings-option unavailable" key={item.href} aria-disabled="true">{content}</div>;
  })}</div></div>;
}
