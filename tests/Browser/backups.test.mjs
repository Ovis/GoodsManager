import test from 'node:test';import assert from 'node:assert/strict';import {fileURLToPath} from 'node:url';
import {build} from '../../src/GoodsManager.Client/node_modules/vite/dist/node/index.js';
import {safeReturn} from '../../src/GoodsManager.Client/src/collection-state.mjs';
const result=await build({configFile:false,root:fileURLToPath(new URL('../../src/GoodsManager.Client/',import.meta.url)),logLevel:'silent',build:{ssr:'src/BackupsStore.ts',write:false}});
const {BackupsStore}=await import('data:text/javascript;base64,'+Buffer.from(result.output.find(x=>x.type==='chunk').code).toString('base64'));
const settings={enabled:true,localTime:'03:00',timeZone:'Asia/Tokyo',automaticRetention:3,destinationDisplay:'test',lastSuccess:null,revision:'1',etag:'old'};
const page={items:[],page:1,pageSize:50,total:0,totalPages:1};
const response=(body,status=200)=>status===204?new Response(null,{status}):new Response(JSON.stringify(body),{status,headers:{'Content-Type':'application/json'}});
function transport(t,handler){const original=globalThis.fetch;globalThis.fetch=handler;t.after(()=>globalThis.fetch=original);}
test('設定412は入力と旧更新条件を保持し、明示見直し後だけ最新条件を使う',async t=>{
 transport(t,async(path,options={})=>path.endsWith('/auth/csrf')?response({token:'csrf'}):options.method==='PUT'?response({code:'REVISION_MISMATCH'},412):response({...settings,revision:'2',etag:'latest',localTime:'04:00'}));
 const w=new BackupsStore();w.settings=settings;w.enabled=false;w.time='05:00';await w.settingsSave();assert.equal(w.conflict,true);await w.fetchLatest();assert.equal(w.time,'05:00');assert.equal(w.settings.etag,'old');w.reviewed();assert.equal(w.settings.etag,'latest');assert.equal(w.time,'05:00');assert.equal(w.conflict,false);
});
test('受付結果不明は新しいキーで作成し直さず、送信内容をそのまま確認する',async t=>{
 let lost=true;const requests=[];transport(t,async(path,options={})=>{if(path.endsWith('/auth/csrf'))return response({token:'csrf'});if(options.method==='POST'){requests.push(options);if(lost)throw new TypeError('lost');return response({id:'run',state:'running'},202);}return response(path.includes('/settings/')?settings:page);});
 const w=new BackupsStore();await w.create();assert.equal(w.save.state,'unknown');await w.create();assert.equal(requests.length,1);lost=false;await w.replay();assert.equal(requests[0].body,requests[1].body);assert.equal(requests[0].headers['Idempotency-Key'],requests[1].headers['Idempotency-Key']);
});
test('進捗更新は編集中の時刻と更新条件を上書きせず、逆転した一覧は採用しない',async t=>{
 const finishes=[];transport(t,async(path)=>path.includes('/settings/')?response({...settings,lastSuccess:'2026-10-18T00:00:00Z'}):new Promise(resolve=>finishes.push(resolve)));
 const w=new BackupsStore();w.settings=settings;w.time='07:00';const a=w.load('?page=1'),b=w.load('?page=2');finishes[1](response({...page,page:2}));await b;finishes[0](response({...page,page:1}));await a;assert.equal(w.data.page,2);assert.equal(w.time,'07:00');assert.equal(w.settings.etag,'old');assert.equal(w.settings.lastSuccess,'2026-10-18T00:00:00Z');
});
test('削除は実行中を拒否し、確認チェックのない要求を送らない',async t=>{
 transport(t,async()=>response({id:'run',state:'running'}));const w=new BackupsStore();await w.openDelete({id:'run'});assert.equal(w.action,null);w.action={data:{id:'other',state:'succeeded'},ack:false};await w.remove();assert.equal(w.save.last,null);
});
test('バックアップからログインしても内部の復帰先だけを許可する',()=>{
 assert.equal(safeReturn('/backups?page=2'),'/backups?page=2');for(const path of ['//example.com/backups','/backups-other','/backups/restore'])assert.equal(safeReturn(path),'/collection');
});
