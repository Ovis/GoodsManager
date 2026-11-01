import test from 'node:test';
import assert from 'node:assert/strict';
import {fileURLToPath} from 'node:url';
import {build} from '../../src/GoodsManager.Client/node_modules/vite/dist/node/index.js';
import {normalize} from '../../src/GoodsManager.Client/photo-ocr/normalize.js';
const root=fileURLToPath(new URL('../../src/GoodsManager.Client/',import.meta.url));
const bundle=await build({configFile:false,root,logLevel:'silent',build:{ssr:'../../tests/Browser/photo-ocr-fixture.ts',write:false}});
const {PhotoOcrClient,PhotoOcrSession,PhotoOcrError,RegistrationStore,copyPhotoText}=await import('data:text/javascript;base64,'+Buffer.from(bundle.output.find(x=>x.type==='chunk').code).toString('base64'));
const turn=()=>new Promise(resolve=>setImmediate(resolve));
const deferred=()=>{let resolve,reject;const promise=new Promise((a,b)=>{resolve=a;reject=b;});return {promise,resolve,reject};};
const file=name=>new File(['sample'],name,{type:'image/png'});
function fixture(t){
 const request=Object.assign(async()=>({}),{accountId:'account',collectionId:'collection'}),store=new RegistrationStore(request);
 store.pump=async()=>{};store.scheduleCheck=()=>{};
 const calls=[],client={checks:0,authorizer:async()=>{},async authorize(){this.checks++;await this.authorizer();},recognize(file,identity,progress){const gate=deferred();calls.push({file,identity,progress,...gate});return gate.promise;},cancel(){}};
 const session=new PhotoOcrSession(store,client);
 t.after(()=>{session.dispose();for(const photo of store.photos)URL.revokeObjectURL(photo.url);});
 return {store,client,session,calls,async add(...names){store.addFiles(names.map(file));await turn();},async done(text='',index=calls.length-1){calls[index].resolve({text});await turn();}};
}

test('複数写真を直列処理し、文字ありでも自動表示せず手動でまとめて表示する',async t=>{
 const f=fixture(t);await f.session.activate();await f.add('a','b','c');assert.equal(f.calls.length,1);
 await f.done('一枚目');assert.equal(f.session.open,false);assert.equal(f.calls.length,2);assert.equal(f.session.entries.length,3);
 f.session.close();await f.done('二枚目');await f.done('三枚目');assert.equal(f.session.open,false);assert.equal(f.session.withText,3);
 await f.session.show();assert.equal(f.session.open,true);assert.ok(f.client.checks>=2);
 await f.add('d');await f.done('四枚目');assert.equal(f.session.open,true);f.session.close();assert.equal(f.session.withText,4);
 await f.add('e');await f.done('五枚目');assert.equal(f.session.open,false);
 assert.equal(f.store.draft.name,'');assert.equal(f.store.draft.memo,'');
});
test('処理待ちから手動表示でき、後続結果を更新し、閉じた後は再表示しない',async t=>{
 const f=fixture(t);await f.session.activate();await f.add('a','b');await f.session.show();assert.equal(f.session.open,true);assert.equal(f.session.withText,0);
 await f.done('一枚目');assert.equal(f.session.open,true);assert.equal(f.session.withText,1);f.session.close();await f.done('後続');assert.equal(f.session.open,false);assert.equal(f.session.withText,2);
});
test('文字なし・認識失敗でも次の写真を処理し、明示再試行では自動表示しない',async t=>{
 const f=fixture(t);await f.session.activate();await f.add('a','b');await f.done('');f.calls[1].reject(new PhotoOcrError('recognize','失敗'));await turn();assert.equal(f.session.open,false);assert.deepEqual(f.session.entries.map(x=>x.state),['empty','error']);
 f.session.retry();await turn();await f.done('再試行成功');await f.done('');assert.equal(f.session.open,false);assert.equal(f.session.withText,1);assert.equal(f.store.photos.length,2);
});
test('準備失敗はキュー全体を止め、再試行まで準備を繰り返さない',async t=>{
 const f=fixture(t);await f.session.activate();await f.add('a','b');f.calls[0].reject(new PhotoOcrError('prepare','準備失敗'));await turn();assert.equal(f.session.blocked,true);assert.equal(f.calls.length,1);
 await f.add('c');assert.equal(f.calls.length,1);f.session.retry();await turn();assert.equal(f.calls.length,2);await f.done('復旧');assert.equal(f.session.open,false);assert.equal(f.calls.length,3);
});
test('アップロード再試行の通知・generation変化でOCRを重複投入せず、実削除だけ反映する',async t=>{
 const f=fixture(t);await f.session.activate();await f.add('a','b');const p=f.store.photos[0];p.generation++;p.state='unknown';f.store.notify();assert.equal(f.calls.length,1);
 await f.store.cancelPhoto(p);assert.equal(f.session.entries.length,2);assert.equal(f.store.photos.length,2);
 p.state='failed';await f.store.cancelPhoto(p);await turn();assert.equal(f.session.entries.length,1);assert.equal(f.calls.length,2);
 f.calls[0].resolve({text:'削除済み'});await turn();assert.equal(f.session.withText,0);await f.done('有効',1);assert.equal(f.session.withText,1);
});
test('画面を離れると停止し、戻っても自動表示しない。停止前の遅延応答は採用しない',async t=>{
 const f=fixture(t);await f.session.activate();await f.add('a','b');f.session.pause();assert.equal(f.session.entries[0].state,'paused');await f.session.activate();assert.equal(f.calls.length,2);
 f.calls[0].resolve({text:'古い'});await turn();assert.equal(f.calls.length,2);assert.equal(f.session.withText,0);
 await f.done('再開',1);assert.equal(f.calls.length,3);await f.done('次',2);assert.equal(f.session.open,false);
});
test('ログアウト・権限喪失では結果だけ破棄し、残る写真は明示操作までOCRしない',async t=>{
 const f=fixture(t);await f.session.activate();await f.add('a');await f.done('非公開');f.session.resetAuthentication();assert.equal(f.session.entries.length,0);assert.equal(f.store.photos.length,1);
 await f.session.activate();assert.equal(f.calls.length,1);f.session.retry();await turn();assert.equal(f.calls.length,2);await f.done('手動');assert.equal(f.session.open,false);
 f.client.authorizer=async()=>{throw new PhotoOcrError('access','権限変更');};await f.session.show();assert.equal(f.session.entries.length,0);assert.equal(f.session.active,false);assert.equal(f.store.photos.length,1);
});
test('結果不明・失敗では保持し、登録の成功が確定した時だけepoch更新と結果破棄',async t=>{
 const f=fixture(t);await f.session.activate();await f.add('a');await f.done('保持');f.store.save.state='unknown';f.store.notify();assert.equal(f.session.withText,1);f.store.save.state='failed';f.store.notify();assert.equal(f.session.withText,1);
 f.store.success({outcome:'created',deduplicatedCount:0,product:{id:'saved'}},{draft:structuredClone(f.store.draft),mode:'next'},()=>{});
 assert.equal(f.store.draftEpoch,1);assert.equal(f.session.entries.length,0);assert.equal(f.session.open,false);assert.equal(f.store.photos.length,0);
});
test('100枚上限で拒否された受け付けはOCRキューに追加せず、別コレクションへ結果を混ぜない',async t=>{
 const a=fixture(t),b=fixture(t);await a.session.activate();await b.session.activate();await a.add(...Array.from({length:101},(_,i)=>String(i)));assert.equal(a.calls.length,0);
 await a.add('A');await b.add('B');await a.done('A結果');assert.equal(b.session.withText,0);assert.equal(b.store.draft.name,'');
});
test('OCR失敗中でも登録要求を送信でき、OCR結果・状態・ファイルをJSONへ含めない',async t=>{
 const f=fixture(t);await f.session.activate();await f.add('a');await f.done('OCRだけの秘密');f.session.entries[0].state='error';
 f.store.draft.name='手入力名';f.store.draft.categoryId='category';f.store.photos[0].state='ready';f.store.photos[0].id='upload';
 let sent;f.store.save.begin=async(path,body)=>{sent=JSON.stringify({path,body});return {outcome:'created',deduplicatedCount:0,product:{id:'saved'}};};
 await f.store.submit('next',()=>{});assert.ok(sent);assert.ok(sent.includes('手入力名'));assert.ok(sent.includes('upload'));assert.ok(!sent.includes('OCRだけの秘密'));assert.ok(!sent.includes('draftEpoch'));assert.ok(!sent.includes('attemptId'));assert.equal(f.session.entries.length,0);
});
test('全文コピーはClipboard APIの不在・拒否時に選択と手動コピー案内へ移る',async()=>{
 const operations=[],textarea={focus(){operations.push('focus');},select(){operations.push('select');}};
 for(const clipboard of [undefined,{writeText:async()=>{throw Error('denied');}}])assert.match(await copyPhotoText('文字',clipboard,textarea),/端末のコピー操作/);
 assert.deepEqual(operations,['focus','select','focus','select']);let copied;assert.match(await copyPhotoText('全文',{writeText:async value=>{copied=value;}},textarea),/コピーしました/);assert.equal(copied,'全文');
});

class FakeWorker {
 sent=[];terminated=false;
 postMessage(message){this.sent.push(message);}
 terminate(){this.terminated=true;}
 reply(extra={},message=this.sent.at(-1)){this.onmessage?.({data:{...message,...extra,done:true}});}
}
const identity={accountId:'a',collectionId:'c',draftEpoch:1,localId:'p',attemptId:0};
function workerFixture(t,limits={prepare:1000,normalize:1000,recognize:1000}){
 const workers=[],access={available:true,assetBase:'/registration-photo-ocr/a/c/'},request=async()=>access;
 const client=new PhotoOcrClient(request,()=>{const w=new FakeWorker();workers.push(w);return w;},limits);t.after(()=>client.cancel());return {client,workers,access};
}
test('WorkerはrequestIdと全identity一致だけを受け付け、正規化→認識し結果の前にも権限確認する',async t=>{
 const f=workerFixture(t),pending=f.client.recognize(file('a'),identity,()=>{});await turn();const w=f.workers[0];
 w.reply({identity:{...identity,attemptId:99}});await turn();assert.equal(w.sent.length,1);
 w.reply({preparationMs:1});await turn();assert.equal(w.sent[1].action,'normalize');w.reply({blob:new Blob(['png']),normalizationMs:2});await turn();assert.equal(w.sent[2].action,'recognize');
 w.reply({text:'  OCR text\n ',recognitionMs:3});assert.equal((await pending).text,'OCR text');
 const next=f.client.recognize(file('b'),identity,()=>{});await turn();assert.equal(f.workers.length,1);assert.equal(w.sent.at(-1).action,'normalize');f.client.cancel();await assert.rejects(next,e=>e.stage==='cancelled');
});
test('Worker取消とタイムアウトは待機Promiseを確定し、次回は別Workerを生成する',async t=>{
 const f=workerFixture(t,{prepare:20,normalize:20,recognize:20});const pending=f.client.recognize(file('a'),identity,()=>{});await assert.rejects(pending,e=>e.stage==='prepare');assert.equal(f.workers[0].terminated,true);
 const next=f.client.recognize(file('b'),identity,()=>{});await turn();assert.equal(f.workers.length,2);f.client.cancel();await assert.rejects(next,e=>e.stage==='cancelled');
 f.workers[0].reply({text:'古い'});assert.equal(f.workers[1].terminated,true);
});
test('prepare完了直後の取消や利用確認中の取消でも後段へ処理を進めない',async t=>{
 const f=workerFixture(t),pending=f.client.recognize(file('a'),identity,()=>{});await turn();f.workers[0].reply({preparationMs:1});f.client.cancel();await assert.rejects(pending,e=>e.stage==='cancelled');assert.equal(f.workers[0].sent.length,1);
 const gate=deferred(),client=new PhotoOcrClient(()=>gate.promise,()=>{throw Error('must not create');});const waiting=client.recognize(file('b'),identity,()=>{});client.cancel();await assert.rejects(waiting,e=>e.stage==='cancelled');gate.resolve({available:true,assetBase:'/'});
});
test('元画像の向き・白背景・縮小上限を指定し、成功・失敗どちらでもbitmapを閉じる',async t=>{
 const saved={bitmap:globalThis.createImageBitmap,canvas:globalThis.OffscreenCanvas};t.after(()=>{globalThis.createImageBitmap=saved.bitmap;globalThis.OffscreenCanvas=saved.canvas;});let closed=0,options,white=false,canvas;
 globalThis.createImageBitmap=async(_,o)=>{options=o;return {width:4000,height:2000,close(){closed++;}};};
 globalThis.OffscreenCanvas=class{constructor(w,h){this.width=w;this.height=h;canvas=this;}getContext(){return {set fillStyle(v){white=v==='#fff';},fillRect(){},drawImage(){}};}async convertToBlob(){return new Blob(['png']);}};
 const result=await normalize(file('a'));assert.deepEqual(options,{imageOrientation:'from-image'});assert.equal(result.width,2048);assert.equal(result.height,1024);assert.equal(white,true);assert.equal(closed,1);assert.equal(canvas.width,0);
 globalThis.createImageBitmap=async()=>({width:20000,height:3000,close(){closed++;}});await assert.rejects(normalize(file('b')));assert.equal(closed,2);
 await assert.rejects(normalize({size:31*1024*1024}));assert.equal(closed,2);
});
