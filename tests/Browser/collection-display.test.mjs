import test from 'node:test';
import assert from 'node:assert/strict';
import {collectionDisplayEntry,collectionDisplayKey,readCollectionDisplay,saveCollectionDisplay} from '../../src/GoodsManager.Client/src/collection-display.mjs';
import {apiSearch,canonicalPage,updateSearch} from '../../src/GoodsManager.Client/src/collection-state.mjs';

const entry=(url,state={})=>({url,state});

function storage(initial='{}'){
    let text=initial;
    return {getItem:key=>key===collectionDisplayKey?text:null,setItem:(key,value)=>{
            assert.equal(key,collectionDisplayKey);

            text=value;
        }};
}

test('表示操作だけを即時保存し、以後の部分更新でも他の表示設定を保持する',()=>{
    const db=storage(),access=()=>db;

    assert.equal(saveCollectionDisplay({view:'table',pageSize:'25',q:'検索語',collectionId:'one'},access),true);
    assert.equal(saveCollectionDisplay({sort:'nameAsc'},access),true);
    assert.equal(saveCollectionDisplay({barcode:'9780306406157',page:'4'},access),true);
    assert.deepEqual(readCollectionDisplay(access),{view:'table',pageSize:'25',sort:'nameAsc'});
    assert.deepEqual(JSON.parse(db.getItem(collectionDisplayKey)),{view:'table',pageSize:'25',sort:'nameAsc'});
});

test('壊れた保存値・旧形式・保存禁止でも起動や画面変更を妨げない',()=>{
    for(const value of ['{','null','[]','42','{"view":"cards","pageSize":"500","sort":"random"}'])assert.deepEqual(readCollectionDisplay(()=>storage(value)),{});

    const denied=()=>{
        throw new DOMException('denied','SecurityError');
    };

    assert.deepEqual(readCollectionDisplay(denied),{});
    assert.equal(saveCollectionDisplay({view:'table'},denied),false);
    assert.equal(saveCollectionDisplay({q:'search'},denied),true);

    const defaults=new URL(collectionDisplayEntry(entry('/collection'),{}).url,'https://local.invalid');

    assert.equal(defaults.searchParams.get('view'),'gallery');
    assert.equal(defaults.searchParams.get('pageSize'),'50');
    assert.equal(defaults.searchParams.has('sort'),false);
});

test('共有URLの明示条件は保存済み既定値より優先し、検索・コレクション・ページは変えない',()=>{
    const saved={view:'table',pageSize:'100',sort:'nameAsc'};
    const url=collectionDisplayEntry(entry('/collection?collectionId=one&q=星&tagIds=a&tagIds=b&page=3&view=gallery&pageSize=25&sort=quantityDesc#results'),saved).url;
    const parsed=new URL(url,'https://local.invalid');

    assert.equal(parsed.searchParams.get('view'),'gallery');
    assert.equal(parsed.searchParams.get('pageSize'),'25');
    assert.equal(parsed.searchParams.get('sort'),'quantityDesc');
    assert.equal(parsed.searchParams.get('q'),'星');
    assert.deepEqual(parsed.searchParams.getAll('tagIds'),['a','b']);
    assert.equal(parsed.searchParams.get('page'),'3');
    assert.equal(parsed.searchParams.get('collectionId'),'one');
    assert.equal(parsed.hash,'#results');
    assert.equal(new URL(collectionDisplayEntry(entry('/collection?view=invalid&sort='),saved).url,'https://local.invalid').searchParams.get('view'),'invalid');
});

test('未指定項目だけ既定値を補い、検索語による関連順の従来動作と一覧以外のURLを維持する',()=>{
    const saved={view:'table',pageSize:'100',sort:'nameAsc'};
    const parsed=new URL(collectionDisplayEntry(entry('/collection?collectionId=two&q=星'),saved).url,'https://local.invalid');

    assert.equal(parsed.searchParams.get('sort'),'nameAsc');
    assert.equal(parsed.searchParams.get('view'),'table');
    assert.equal(parsed.searchParams.get('collectionId'),'two');

    const automatic=new URL(collectionDisplayEntry(entry('/collection?q=星'),{}).url,'https://local.invalid');

    assert.equal(automatic.searchParams.has('sort'),false);

    for(const path of ['/register','/collection-other','/products/id','/settings','/login']){
        const original=entry(path);

        assert.equal(collectionDisplayEntry(original,saved),original);
    }
});

test('既存履歴は後から保存した既定値で上書きせず、スクロール・入力途中・条件を保持する',()=>{
    const original=collectionDisplayEntry(entry('/collection?q=星&page=3',{key:'old',scroll:420,draftQ:'検索途中'}),{});
    const newer={view:'table',pageSize:'100',sort:'nameAsc'};

    assert.equal(collectionDisplayEntry(original,newer),original);
    assert.equal(original.state.key,'old');
    assert.equal(original.state.scroll,420);
    assert.equal(original.state.draftQ,'検索途中');
    assert.equal(new URL(original.url,'https://local.invalid').searchParams.has('sort'),false);

    const next=collectionDisplayEntry(entry('/collection?collectionId=two'),newer);

    assert.equal(new URL(next.url,'https://local.invalid').searchParams.get('sort'),'nameAsc');

    const changed=updateSearch(new URL(original.url,'https://local.invalid').search,{view:'table'});

    assert.equal(new URLSearchParams(changed).get('page'),'3');
    assert.equal(new URLSearchParams(updateSearch(changed,{pageSize:'100'})).has('page'),false);
    assert.equal(new URLSearchParams(apiSearch(changed)).has('view'),false);
    assert.equal(new URLSearchParams(canonicalPage(changed,2)).get('page'),'2');
});
