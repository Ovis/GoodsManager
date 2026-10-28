import test from 'node:test';
import assert from 'node:assert/strict';
import {fileURLToPath} from 'node:url';
import {build} from '../../src/GoodsManager.Client/node_modules/vite/dist/node/index.js';
import {holdingLabel} from '../../src/GoodsManager.Client/src/holding-label.ts';
import {accountError} from '../../src/GoodsManager.Client/src/account-state.mjs';

async function clientModule(entry){
  const result=await build({configFile:false,root:fileURLToPath(new URL('../../src/GoodsManager.Client/',import.meta.url)),logLevel:'silent',build:{ssr:entry,write:false}});
  return import('data:text/javascript;base64,'+Buffer.from(result.output.find(x=>x.type==='chunk').code).toString('base64'));
}
const {loadPickerNames,loadPickerPage}=await clientModule('src/master-picker-data.ts');
const {ApiError,errorText}=await clientModule('src/api.ts');
const json=(body,status=200)=>new Response(JSON.stringify(body),{status,headers:{'Content-Type':'application/json'}});
function transport(t,handler){const previous=globalThis.fetch;globalThis.fetch=handler;t.after(()=>{globalThis.fetch=previous;});}

test('検索・ページ送りの条件と選択用IDを保ったまま、ページ外の親から名前を解決する',async t=>{
  const calls=[];
  transport(t,async(path,options)=>{calls.push({path,options});return path.endsWith('/categories/tree')?json({nodes:[{id:'child',name:'その他',parentId:'parent'},{id:'parent',name:'紙・印刷物',parentId:null}]}):json({items:[{id:'child',name:'その他'}],totalPages:3});});
  const signal=new AbortController().signal;
  const result=await loadPickerPage('categories',new URLSearchParams({q:'その他',page:'2',pageSize:'25'}),signal);
  assert.deepEqual(result.page.items,[{id:'child',name:'紙・印刷物 › その他'}]);
  assert.equal(result.page.totalPages,3);
  const url=new URL('https://localhost'+calls.find(x=>x.path.includes('/masters/')).path);
  assert.equal(url.searchParams.get('q'),'その他');assert.equal(url.searchParams.get('page'),'2');assert.equal(url.searchParams.get('pageSize'),'25');
  assert.ok(calls.every(x=>x.options.signal===signal));
});

test('削除済みカテゴリは内部IDを表示せず、残るカテゴリの名前も失わない',async t=>{
  transport(t,async()=>json({nodes:[{id:'live',name:'書籍',parentId:null}]}));
  assert.deepEqual(await loadPickerNames('categories',['live','gone'],new AbortController().signal),{live:'書籍',gone:'削除済みのカテゴリ'});
});

test('関連差分に削除済みのマスターが混じっても、取得できた名前を表示する',async t=>{
  transport(t,async path=>path.endsWith('/gone')?json({code:'NOT_FOUND'},404):json({id:'live',name:'星巡りの旅'}));
  assert.deepEqual(await loadPickerNames('works',['live','gone'],new AbortController().signal),{live:'星巡りの旅',gone:'削除済みの項目'});
});

test('通信失敗や中断を削除済みと誤表示せず、再取得できるエラーにする',async t=>{
  transport(t,async()=>json({code:'SERVER_ERROR'},500));
  await assert.rejects(loadPickerNames('works',['live'],new AbortController().signal));
  globalThis.fetch=async()=>{throw new DOMException('aborted','AbortError');};
  await assert.rejects(loadPickerNames('works',['live'],new AbortController().signal),{name:'AbortError'});
});

test('同じ属性の個体も並び番号で区別し、一覧にない参照をIDや個体0で表示しない',()=>{
  const rows=[{id:'11111111-1111-1111-1111-111111111111'},{id:'22222222-2222-2222-2222-222222222222'}];
  assert.equal(holdingLabel('個体',rows,rows[0].id),'個体 1');assert.equal(holdingLabel('個体',rows,rows[1].id),'個体 2');
  assert.equal(holdingLabel('個体',rows,'missing'),'選択した個体');
});

test('エラーの照会IDは診断用オブジェクトに保持し、利用者向けの説明には含めない',()=>{
  const trace='11111111-1111-1111-1111-111111111111',error=new ApiError(400,'VALIDATION_FAILED',trace);
  assert.equal(error.traceId,trace);assert.ok(!errorText(error).includes(trace));assert.ok(!accountError(error).includes(trace));
});
