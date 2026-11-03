export const collectionDisplayKey='goods.collection-display.v1';

export const collectionSortNames={relevance:'関連順',createdDesc:'登録日が新しい順',createdAsc:'登録日が古い順',updatedDesc:'更新日が新しい順',nameAsc:'商品名 昇順',nameDesc:'商品名 降順',quantityDesc:'所持数が多い順',quantityAsc:'所持数が少ない順'};

const choices={view:['gallery','table'],pageSize:['25','50','100'],sort:Object.keys(collectionSortNames)};

/** 保存値を検索条件や任意のURLとして扱わず、既知の表示設定だけを受け付ける。 */
function validValues(value){
    /** @type {Record<string,string>} */ const result={};
    if(!value||typeof value!=='object'||Array.isArray(value))return result;

    for(const [key,values] of Object.entries(choices))if(typeof value[key]==='string'&&values.includes(value[key]))result[key]=value[key];

    return result;
}

export function readCollectionDisplay(storage=()=>globalThis.localStorage){
    try{
        return validValues(JSON.parse(storage().getItem(collectionDisplayKey)||'{}'));
    }catch{
        return {};
    }
}

export function saveCollectionDisplay(changes,storage=()=>globalThis.localStorage){
    const values=validValues(changes);
    if(!Object.keys(values).length)return true;

    try{
        storage().setItem(collectionDisplayKey,JSON.stringify({...readCollectionDisplay(storage),...values}));
        return true;
    }catch{
        return false;
    }
}

/**
* 新しい一覧の履歴にだけ既定値を適用する。明示URLと既存履歴は変更しない。
* 並び順の未設定は保持し、従来どおり検索語ありでは関連順、なしでは登録日順にする。
* @param {{url:string,state:Record<string,unknown>}} entry
* @param {Record<string,string>|undefined} preferences
*/
export function collectionDisplayEntry(entry,preferences=undefined){
    const target=new URL(entry.url,'https://local.invalid');
    if(target.pathname!=='/collection'||entry.state.collectionDisplayResolved===true)return entry;

    const values={view:'gallery',pageSize:'50',...validValues(preferences??readCollectionDisplay())};
    for(const [key,value] of Object.entries(values))if(!target.searchParams.has(key))target.searchParams.set(key,value);

    return {url:target.pathname+target.search+target.hash,state:{...entry.state,collectionDisplayResolved:true}};
}
