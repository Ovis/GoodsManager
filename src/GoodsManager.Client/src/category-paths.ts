export type CategoryNode = {id:string;name:string;parentId:string|null};

/** ページ外の祖先も使い、同名の子カテゴリを親からの経路で区別する。 */
export function categoryPaths(nodes:CategoryNode[]):Record<string,string> {
  const byId=new Map(nodes.map(node=>[node.id,node]));
  const paths:Record<string,string>={};
  for(const node of nodes){
    const chain:CategoryNode[]=[],seen=new Set<string>();
    let current:CategoryNode|undefined=node;
    while(current&&!paths[current.id]){
      if(seen.has(current.id))throw new Error('カテゴリの親子関係が循環しています。');
      seen.add(current.id);chain.push(current);
      if(current.parentId===null){current=undefined;break;}
      const parent=byId.get(current.parentId);
      if(!parent)throw new Error('親カテゴリを取得できませんでした。');
      current=parent;
    }
    let path=current?paths[current.id]:'';
    for(const item of chain.reverse()){
      path=path?`${path} › ${item.name}`:item.name;
      paths[item.id]=path;
    }
  }
  return paths;
}
