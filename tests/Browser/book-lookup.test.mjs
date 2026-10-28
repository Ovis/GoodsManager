import test from 'node:test';
import assert from 'node:assert/strict';
import {fileURLToPath} from 'node:url';
import {build} from '../../src/GoodsManager.Client/node_modules/vite/dist/node/index.js';
const result=await build({configFile:false,root:fileURLToPath(new URL('../../src/GoodsManager.Client/',import.meta.url)),logLevel:'silent',build:{ssr:'src/BookLookupStore.ts',write:false}});
const {BookLookupStore,validIsbn}=await import('data:text/javascript;base64,'+Buffer.from(result.output.find(x=>x.type==='chunk').code).toString('base64'));
const storeBundle=await build({configFile:false,root:fileURLToPath(new URL('../../src/GoodsManager.Client/',import.meta.url)),logLevel:'silent',build:{ssr:'src/RegistrationStore.ts',write:false}});
const {RegistrationStore}=await import('data:text/javascript;base64,'+Buffer.from(storeBundle.output.find(x=>x.type==='chunk').code).toString('base64'));
const match={id:'publisher',name:'新潮社',websiteUrl:null,memo:null,createdAt:'2026-10-28T00:00:00Z'};
const book=(extra={})=>({title:'こころ 上',volume:'上',edition:'改版',authors:['夏目 漱石'],publishers:[{name:'新潮社',matches:[match]}],publicationYear:'2004',sourceUrl:'https://ndlsearch.ndl.go.jp/books/R100000002-I000007577929',inProcess:false,...extra});
const deferred=()=>{let resolve,reject;const promise=new Promise((a,b)=>{resolve=a;reject=b;});return {promise,resolve,reject};};
function fixture(t,transport=async()=>({items:[book()]})){
  const store=new RegistrationStore();store.scheduleCheck=()=>{};store.draft.identifiers=[{scheme:'isbn13',value:'9784101010137'}];
  const calls=[];const lookup=new BookLookupStore(store,0,()=>false,async(path,options)=>{calls.push({path,options});return transport(path,options);});
  const disconnect=lookup.connect();t.after(disconnect);return {store,lookup,calls,disconnect};
}

test('ISBNの種類と検算を満たす場合だけ書籍検索でき、カメラで値を変更しても自動照会しない',async t=>{
  for(const scheme of ['ean13','ean8','upca'])assert.equal(validIsbn({scheme,value:'9784101010137'}),false);
  for(const value of ['','9784101010138','978410101013'])assert.equal(validIsbn({scheme:'isbn13',value}),false);
  assert.equal(validIsbn({scheme:'isbn13',value:' '.repeat(201)+'9784101010137'}),false);
  assert.equal(validIsbn({scheme:'isbn10',value:'４-１０-１０１０１３-７'}),true);
  const {store,lookup,calls}=fixture(t);store.update('identifiers',[{scheme:'isbn13',value:'9780306406157'}]);assert.equal(calls.length,0);
  store.update('identifiers',[{scheme:'ean13',value:'9780306406157'}]);await lookup.search();assert.equal(calls.length,0);
});

test('1件なら空欄を補完し、既存の出版社を再利用して出典を追加する。取得に書込要求は使わない',async t=>{
  const {store,lookup,calls}=fixture(t);await lookup.search();
  assert.equal(store.draft.name,'こころ 上');assert.equal(store.draft.memo,'著者：夏目 漱石\n出版年：2004年');assert.equal(store.draft.manufacturerId,'publisher');assert.equal(store.names.publisher,'新潮社');assert.deepEqual(store.draft.links,[book().sourceUrl]);
  assert.equal(calls.length,1);assert.equal(calls[0].path,'/book-metadata?isbn=9784101010137');assert.equal(calls[0].options.method,undefined);assert.equal(Object.keys(store.masterJobs).length,0);
});

test('検索待ちに入力した商品名・メモ・製造元を上書きせず、既存リンクも保持する',async t=>{
  const pending=deferred(),{store,lookup}=fixture(t,()=>pending.promise);const search=lookup.search();
  store.update('name','手入力の商品名');store.update('memo','自分のメモ');store.update('manufacturerId','manual');store.update('links',['https://example.com/']);
  pending.resolve({items:[book()]});await search;
  assert.equal(store.draft.name,'手入力の商品名');assert.equal(store.draft.memo,'自分のメモ');assert.equal(store.draft.manufacturerId,'manual');assert.deepEqual(store.draft.links,['https://example.com/',book().sourceUrl]);
});

test('複数件は選択まで反映せず、取消でも入力を変えない',async t=>{
  const books=[book(),book({title:'別版',sourceUrl:'https://ndlsearch.ndl.go.jp/books/R100000002-I000000002'})],{store,lookup}=fixture(t,async()=>({items:books}));
  const original=structuredClone(store.draft);await lookup.search();assert.deepEqual(store.draft,original);assert.equal(lookup.candidates.length,2);lookup.dismiss();assert.deepEqual(store.draft,original);
  await lookup.search();lookup.choose(books[1]);assert.equal(store.draft.name,'別版');assert.equal(lookup.candidates.length,0);
});

test('同名組織が複数なら明示選択まで製造元を設定せず、途中の製造元入力も保持する',async t=>{
  const b=book({publishers:[{name:'新潮社',matches:[match,{...match,id:'publisher2',memo:'別の組織'}]}]});
  const {store,lookup}=fixture(t,async()=>({items:[b]}));await lookup.search();assert.equal(store.draft.manufacturerId,'');
  lookup.choosePublisher(b.publishers[0].matches[1]);assert.equal(store.draft.manufacturerId,'publisher2');
  store.update('manufacturerId','manual');lookup.choosePublisher(match);assert.equal(store.draft.manufacturerId,'manual');
});

test('未登録の出版社は作成候補として保持し、検索だけで組織を増やさない',async t=>{
  const {store,lookup,calls}=fixture(t,async()=>({items:[book({publishers:[{name:'未登録出版',matches:[]}]})]}));await lookup.search();
  assert.equal(store.draft.manufacturerId,'');assert.equal(lookup.publisherBook.publishers[0].name,'未登録出版');assert.equal(calls.length,1);assert.deepEqual(store.masterJobs,{});assert.equal(lookup.canApplyPublisher(),true);
  store.update('identifiers',[{scheme:'isbn13',value:'9780306406157'}]);assert.equal(lookup.canApplyPublisher(),false);
});

test('出版社が複数ある書誌では、1社を勝手に決めない',async t=>{
  const {store,lookup}=fixture(t,async()=>({items:[book({publishers:[{name:'新潮社',matches:[match]},{name:'別社',matches:[{...match,id:'other'}]}]})]}));await lookup.search();assert.equal(store.draft.manufacturerId,'');
});

test('明示的な出版社作成中も候補を保持し、作成完了後は空欄への選択を許可する',async t=>{
  const {store,lookup}=fixture(t,async()=>({items:[book({publishers:[{name:'未登録出版',matches:[]}]})]}));await lookup.search();
  store.masterJobs.manufacturerId={create:{state:'sending'},relation:{state:'idle'},works:[]};store.notify();assert.equal(lookup.canApplyPublisher(),false);assert.ok(lookup.publisherBook);
  store.masterJobs.manufacturerId.create.state='success';store.masterJobs.manufacturerId.result=match;store.notify();assert.equal(lookup.canApplyPublisher(),true);
});

test('該当なし・通信失敗は入力を保持し、明示的に再試行できる',async t=>{
  let failures=true;const {store,lookup}=fixture(t,async()=>{if(failures)throw new Error('offline');return {items:[]};});const original=structuredClone(store.draft);
  await lookup.search();assert.ok(lookup.error);assert.deepEqual(store.draft,original);failures=false;await lookup.search();assert.equal(lookup.error,'');assert.match(lookup.notice,/見つかりません/);assert.deepEqual(store.draft,original);
});

test('ISBNをA→B→Aと変えても旧応答は反映せず、新しい応答だけ採用する',async t=>{
  const old=deferred(),fresh=deferred();let count=0;const {store,lookup,calls}=fixture(t,()=>++count===1?old.promise:fresh.promise);
  const first=lookup.search();store.update('identifiers',[{scheme:'isbn13',value:'9780306406157'}]);store.update('identifiers',[{scheme:'isbn13',value:'9784101010137'}]);const second=lookup.search();
  assert.equal(calls[0].options.signal.aborted,true);fresh.resolve({items:[book({title:'最新'})]});await second;old.resolve({items:[book({title:'古い'})]});await first;assert.equal(store.draft.name,'最新');
});

test('画面移動や保存開始で検索を中断し、遅れた応答を別の商品に反映しない',async t=>{
  for(const stop of ['disconnect','save']){
    const pending=deferred(),f=fixture(t,()=>pending.promise);const search=f.lookup.search();
    if(stop==='disconnect')f.disconnect();else {f.store.busy=true;f.store.notify();f.store.busy=false;f.store.notify();}
    pending.resolve({items:[book()]});await search;assert.equal(f.store.draft.name,'');assert.equal(f.lookup.publisherBook,null);assert.equal(f.calls[0].options.signal.aborted,true);
  }
});

test('同じ出典URLを重複追加せず、作成中書誌と入力上限超過を明示する',async t=>{
  const {store,lookup}=fixture(t,async()=>({items:[book({title:'長'.repeat(201),inProcess:true})]}));store.draft.links=['https://example.com/',book().sourceUrl];await lookup.search();
  assert.equal(store.draft.name,'');assert.equal(store.draft.links.length,2);assert.match(lookup.notice,/作成中/);assert.match(lookup.notice,/上限/);
});
