import test from 'node:test';
import assert from 'node:assert/strict';
import {fileURLToPath} from 'node:url';
import {build} from '../../src/GoodsManager.Client/node_modules/vite/dist/node/index.js';
async function clientModule(entry){const result=await build({configFile:false,root:fileURLToPath(new URL('../../src/GoodsManager.Client/',import.meta.url)),logLevel:'silent',build:{ssr:entry,write:false}});return import('data:text/javascript;base64,'+Buffer.from(result.output.find(x=>x.type==='chunk').code).toString('base64'));}
const {apiForCollection,apiForAccount}=await clientModule('src/api.ts');
const {RegistrationStore,mutation}=await clientModule('src/RegistrationStore.ts');
const {CollectionsStore}=await clientModule('src/CollectionsStore.ts');
const deferred=()=>{let resolve;const promise=new Promise(a=>resolve=a);return {promise,resolve};};
function fetcher(t,fn){const original=globalThis.fetch;globalThis.fetch=fn;t.after(()=>globalThis.fetch=original);}
const json=value=>new Response(JSON.stringify(value),{status:200,headers:{'Content-Type':'application/json'}});

test('CSRFの待機中に別コレクションを開いても、送信先と対象アカウントは変えない',async t=>{
  const csrf=deferred(),calls=[];fetcher(t,async(path,options)=>{if(path.endsWith('/auth/csrf'))return csrf.promise;calls.push({path,options});return json({});});
  const a=apiForCollection('A','owner'),b=apiForCollection('B','owner');const pending=a('/products',{method:'POST',body:'{}'});await b('/products?collectionId=B&q=検索');csrf.resolve(json({token:'token'}));await pending;
  assert.equal(calls[0].path,'/api/v1/products?q=%E6%A4%9C%E7%B4%A2');assert.equal(calls[0].options.headers.get('X-Collection-Id'),'B');assert.equal(calls[1].options.headers.get('X-Collection-Id'),'A');assert.equal(calls[1].options.headers.get('X-Account-Id'),'owner');
});

test('保存結果不明の再送は元のコレクション・アカウント・キー・本文を維持する',async t=>{
  const sent=[];fetcher(t,async(path,options)=>{if(path.endsWith('/auth/csrf'))return json({token:'token'});sent.push(options);if(sent.length===1)throw new Error('lost');return json({id:'saved'});});
  const a=apiForCollection('A','owner'),state=mutation(a);await assert.rejects(state.begin('/products',{name:'Aの商品'},'etag'));
  apiForCollection('B','different-owner');await state.replay();assert.equal(sent[0].body,sent[1].body);assert.equal(sent[0].headers['Idempotency-Key'],sent[1].headers['Idempotency-Key']);assert.equal(sent[1].headers['X-Collection-Id'],'A');assert.equal(sent[1].headers['X-Account-Id'],'owner');assert.equal(sent[1].headers['If-Match'],'etag');
});

test('商品登録の下書きと表示名キャッシュはコレクション別に持つ',()=>{
  const a=new RegistrationStore(apiForCollection('A','owner')),b=new RegistrationStore(apiForCollection('B','owner'));a.scheduleCheck=()=>{};b.scheduleCheck=()=>{};
  a.update('name','Aの下書き');b.update('name','Bの下書き');a.resolved({same:'Aのカテゴリ'});b.resolved({same:'Bのカテゴリ'});
  assert.equal(a.draft.name,'Aの下書き');assert.equal(b.draft.name,'Bの下書き');assert.equal(a.names.same,'Aのカテゴリ');assert.equal(b.names.same,'Bのカテゴリ');assert.equal(a.request.collectionId,'A');
});

test('コレクション管理の要求はアカウントに結び付け、設定コピーを明示確認する',async t=>{
  const calls=[];fetcher(t,async(path,options)=>{if(path.endsWith('/auth/csrf'))return json({token:'token'});calls.push({path,options});return json({id:'new',name:'コピー先',role:'manager',revision:'1',etag:'etag'});});
  const state=new CollectionsStore(apiForAccount('owner'));state.name='コピー先';state.importFrom='source';await state.submit();assert.equal(calls.length,0);state.ack=true;await state.submit();assert.equal(calls[0].options.headers['X-Account-Id'],'owner');assert.deepEqual(JSON.parse(calls[0].options.body),{name:'コピー先',importFrom:'source'});assert.equal(state.items[0].id,'new');
});

const {MasterRequests}=await import('../../src/GoodsManager.Web/Development/MasterCheck/requests.mjs');
test('開発確認画面でも送信済み要求の所属とアカウントを変えない',async()=>{
  let owner='one',collection='A';const calls=[];
  const state=new MasterRequests({fetcher:async(path,options)=>{calls.push({path,options});if(path.endsWith('/auth/session'))return json({user:{id:owner}});if(path.endsWith('/collections'))return json({items:[{id:collection,name:collection}]});if(path.endsWith('/auth/csrf'))return json({token:'token'});return json({id:'saved'});}});
  await state.connect();await state.write('masters/tags','POST',{name:'Aのタグ'});owner='two';collection='B';await state.connect();await state.replay();const sent=calls.filter(x=>x.options.method==='POST');assert.equal(sent[0].options.headers['X-Collection-Id'],'A');assert.equal(sent[1].options.headers['X-Collection-Id'],'A');assert.equal(sent[1].options.headers['X-Account-Id'],'one');
});
test('複数ある開発確認先を暗黙の先頭項目にしない',async()=>{
 const state=new MasterRequests({fetcher:async path=>json(path.endsWith('/auth/session')?{user:{id:'one'}}:{items:[{id:'A'},{id:'B'}]})});await assert.rejects(state.connect(),/collectionId/);assert.equal(state.context,null);
});
