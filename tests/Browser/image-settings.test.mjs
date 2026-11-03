import test from 'node:test';import assert from 'node:assert/strict';import {fileURLToPath} from 'node:url';
import {build} from '../../src/GoodsManager.Client/node_modules/vite/dist/node/index.js';
const bundle=await build({configFile:false,root:fileURLToPath(new URL('../../src/GoodsManager.Client/',import.meta.url)),logLevel:'silent',build:{ssr:'src/ImageSettingsStore.ts',write:false}});
const {ImageSettingsStore}=await import('data:text/javascript;base64,'+Buffer.from(bundle.output.find(x=>x.type==='chunk').code).toString('base64'));
const settings=(overrides={})=>({keepOriginal:false,displayMaxEdge:2048,displayQuality:82,thumbMaxEdge:320,thumbQuality:75,uploadMaxBytes:31457280,maxPixels:40000000,maxEdge:16384,temporaryMaxBytes:2147483648,temporaryMaxCount:100,conversionTimeoutSeconds:30,uploadLifetimeHours:24,revision:'1',etag:'"settings:images:1"',...overrides});
function request(read=()=>settings(),collectionId='A'){return Object.assign(async path=>path==='/auth/csrf'?{token:'csrf'}:read(),{collectionId,accountId:'owner'});}
function transport(t,send){const old=globalThis.fetch;globalThis.fetch=send;t.after(()=>globalThis.fetch=old);}
const json=(value,status=200)=>new Response(JSON.stringify(value),{status,headers:{'Content-Type':'application/json'}});
test('コレクション別に入力を保持し、再表示や最新内容の取得で入力を上書きしない',async()=>{
 const a=new ImageSettingsStore(request()),b=new ImageSettingsStore(request(()=>settings({displayQuality:60}),'B'));await a.load();await b.load();a.update('displayQuality','85');await a.load();await a.fetchLatest();
 assert.equal(a.draft.displayQuality,'85');assert.equal(a.latest.displayQuality,82);assert.equal(b.draft.displayQuality,'60');assert.equal(a.dirty,true);a.reset();assert.equal(a.dirty,false);
});
test('画像設定の結果不明では入力を固定し、本文・キー・ETag・コレクションを保持して再送する',async t=>{
 const store=new ImageSettingsStore(request());await store.load();store.update('keepOriginal',true);const sent=[];let fail=true;
 transport(t,async(path,options)=>{sent.push({path,...options});if(fail)throw new TypeError('offline');return json(settings({keepOriginal:true,revision:'2',etag:'"settings:images:2"'}));});
 await store.submit();assert.equal(store.save.state,'unknown');store.update('displayQuality','90');assert.equal(store.draft.displayQuality,'82');fail=false;await store.replay();
 assert.equal(sent[0].body,sent[1].body);for(const key of ['If-Match','Idempotency-Key','X-Collection-Id','X-Account-Id'])assert.equal(sent[0].headers[key],sent[1].headers[key]);
 assert.equal(sent[1].headers['X-Collection-Id'],'A');assert.equal(sent[1].headers['If-Match'],'"settings:images:1"');assert.equal(store.current.keepOriginal,true);assert.equal(store.dirty,false);
});
test('412で入力と旧ETagを保持し、最新内容との明示的な見直し後だけ保存を許可する',async t=>{
 let latest=settings(),sent=0;const store=new ImageSettingsStore(request(()=>latest));await store.load();store.update('keepOriginal',true);
 transport(t,async()=>{sent++;return json({code:'REVISION_MISMATCH'},412);});await store.submit();assert.equal(store.conflict,true);assert.equal(store.current.etag,'"settings:images:1"');
 latest=settings({displayQuality:70,revision:'2',etag:'"settings:images:2"'});await store.fetchLatest();await store.submit();assert.equal(sent,1);assert.equal(store.draft.displayQuality,'82');
 store.reviewed();assert.equal(store.current.etag,'"settings:images:2"');assert.equal(store.draft.keepOriginal,true);assert.equal(store.conflict,false);
});
test('サイズ・品質の範囲、整数、サムネイルと表示用サイズの大小を通信前に検査する',async t=>{
 let sent=0;transport(t,async()=>{sent++;return json(settings());});const store=new ImageSettingsStore(request());await store.load();
 for(const [key,value] of [['displayMaxEdge','4097'],['displayMaxEdge','0'],['thumbMaxEdge','2049'],['thumbQuality','1.5'],['displayQuality',''],['thumbQuality','101']]){store.reset();store.update(key,value);await store.submit();assert.ok(store.error);}
 assert.equal(sent,0);
});
