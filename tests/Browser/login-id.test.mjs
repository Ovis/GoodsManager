import test from 'node:test';import assert from 'node:assert/strict';import {fileURLToPath} from 'node:url';
import {build} from '../../src/GoodsManager.Client/node_modules/vite/dist/node/index.js';
const bundle=await build({configFile:false,root:fileURLToPath(new URL('../../src/GoodsManager.Client/',import.meta.url)),logLevel:'silent',build:{ssr:'src/LoginIdStore.ts',write:false}});
const {LoginIdStore,loginIdError}=await import('data:text/javascript;base64,'+Buffer.from(bundle.output.find(x=>x.type==='chunk').code).toString('base64'));
const user=(loginId='owner',etag='"one"')=>({id:'owner-id',loginId,isAdministrator:true,etag});
const json=(body,status=200)=>new Response(JSON.stringify(body),{status,headers:{'Content-Type':'application/json'}});
function fixture(t,put){let current=user();const calls=[],updated=[],old=globalThis.fetch;globalThis.fetch=async(path,options={})=>{calls.push({path,...options});if(path.endsWith('/auth/session'))return json({user:current});if(path.endsWith('/auth/csrf'))return json({token:'csrf'});return put(path,options,value=>current=value);};t.after(()=>globalThis.fetch=old);return {store:new LoginIdStore(undefined,value=>updated.push(value)),calls,updated,setCurrent:value=>current=value};}
test('ログインIDは既存APIと同じASCII文字種と1〜64文字で検査し、ETag付きで保存して表示を更新する',async t=>{
 for(const invalid of ['', 'a b','日本語','x'.repeat(65)])assert.ok(loginIdError(invalid));assert.equal(loginIdError('Owner._-12'),'');
 const f=fixture(t,async(path,options,set)=>{assert.equal(path,'/api/v1/account');assert.equal(options.method,'PUT');assert.equal(options.headers.get('If-Match'),'"one"');assert.equal(options.headers.get('X-CSRF-Token'),'csrf');assert.deepEqual(JSON.parse(options.body),{loginId:'new.owner'});const value=user('new.owner','"two"');set(value);return json({user:value});});
 await f.store.load();f.store.update('new.owner');await f.store.submit();assert.equal(f.store.current.loginId,'new.owner');assert.equal(f.updated.at(-1).loginId,'new.owner');assert.equal(f.store.dirty,false);assert.match(f.store.notice,/ログイン状態は維持/);
});
test('競合で入力と旧ETagを保ち、最新内容との見直し前に再送しない',async t=>{
 let writes=0;const f=fixture(t,async()=>{writes++;return json({code:'REVISION_MISMATCH'},412);});await f.store.load();f.store.update('draft');await f.store.submit();assert.equal(f.store.conflict,true);
 f.setCurrent(user('other','"two"'));await f.store.load();assert.equal(f.store.name,'draft');assert.equal(f.store.current.etag,'"one"');await f.store.submit();assert.equal(writes,1);
 f.store.reviewed();assert.equal(f.store.current.etag,'"two"');assert.equal(f.store.name,'draft');assert.equal(f.store.conflict,false);
});
test('結果不明では自動再送せず、最新IDが送信値と一致したときだけ保存済みとして確定する',async t=>{
 let writes=0;const f=fixture(t,async(path,options,set)=>{writes++;set(user('changed','"two"'));throw new TypeError('offline');});await f.store.load();f.store.update('changed');await f.store.submit();assert.equal(f.store.uncertain,true);f.store.update('another');assert.equal(f.store.name,'changed');await f.store.submit();assert.equal(writes,1);
 await f.store.load();assert.equal(f.store.uncertain,false);assert.equal(f.store.current.loginId,'changed');assert.match(f.store.notice,/保存されていることを確認/);assert.equal(writes,1);
});
test('不明結果が未反映なら入力を保持して見直しを求め、入力不正では通信しない',async t=>{
 let writes=0;const f=fixture(t,async()=>{writes++;throw new TypeError('offline');});await f.store.load();f.store.update('invalid value');await f.store.submit();assert.equal(writes,0);f.store.update('pending');await f.store.submit();await f.store.load();assert.equal(f.store.name,'pending');assert.equal(f.store.latest.loginId,'owner');assert.equal(f.store.conflict,true);assert.equal(f.store.uncertain,false);
});
