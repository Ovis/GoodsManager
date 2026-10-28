import test from 'node:test';
import assert from 'node:assert/strict';
import {fileURLToPath} from 'node:url';
import {build} from '../../src/GoodsManager.Client/node_modules/vite/dist/node/index.js';
import {barcodeValue,StableBarcode,barcodeCameraError} from '../../src/GoodsManager.Client/src/barcode-result.mjs';

const bundle=await build({configFile:false,root:fileURLToPath(new URL('../../src/GoodsManager.Client/',import.meta.url)),logLevel:'silent',build:{ssr:'src/barcode-scanner.ts',write:false}});
const {BarcodeScannerSession}=await import('data:text/javascript;base64,'+Buffer.from(bundle.output.find(x=>x.type==='chunk').code).toString('base64'));
const result=(code='4006381333931',format='ean_13')=>({codeResult:{code,format}});
const deferred=()=>{let resolve,reject;const promise=new Promise((a,b)=>{resolve=a;reject=b;});return {promise,resolve,reject};};
const tick=()=>new Promise(resolve=>setImmediate(resolve));

test('認識した商品バーコードを検算し、番号の種類も決める',()=>{
  assert.deepEqual(barcodeValue(result()),{scheme:'ean13',value:'4006381333931'});
  assert.deepEqual(barcodeValue(result('96385074','ean_8')),{scheme:'ean8',value:'96385074'});
  assert.deepEqual(barcodeValue(result('036000291452','upc_a')),{scheme:'upca',value:'036000291452'});
  assert.deepEqual(barcodeValue(result('0036000291452'),'upca'),{scheme:'upca',value:'036000291452'});
  for(const value of ['','4006381333932','12345','4006381333931x'])assert.equal(barcodeValue(result(value)),null);
  assert.equal(barcodeValue(result('4006381333931','code_128')),null);
  assert.equal(barcodeValue({}),null);
});

test('書籍はISBN-13を入力し、ISBN選択時は価格や一般商品のバーコードを拒否する',()=>{
  for(const preferred of ['ean13','isbn10','isbn13'])assert.deepEqual(barcodeValue(result('9780306406157'),preferred),{scheme:'isbn13',value:'9780306406157'});
  assert.equal(barcodeValue(result(),'isbn13'),null);
  assert.equal(barcodeValue(result('96385074','ean_8'),'isbn10'),null);
});

test('同じ有効コードが連続する別フレームで一致した場合だけ確定する',()=>{
  const stable=new StableBarcode();
  assert.equal(stable.accept(result(),100),null);
  assert.equal(stable.accept({},200),null);
  assert.equal(stable.accept(result(),300),null);
  assert.equal(stable.accept(result('96385074','ean_8'),400),null);
  assert.equal(stable.accept(result(),500),null);
  assert.equal(stable.accept(result(),1600),null);
  assert.equal(stable.accept(result(),1600),null);
  assert.deepEqual(stable.accept(result(),1700),{scheme:'ean13',value:'4006381333931'});
});

function fixture({init=async()=>{},stop=async()=>{},load}={}){
  const events=[],track=new EventTarget();track.stops=0;track.stop=()=>{track.stops++;events.push('track.stop');};
  const stream={getTracks:()=>[track]},video=new EventTarget();video.srcObject=null;video.pause=()=>events.push('pause');
  let handler=null;
  const engine={CameraAccess:{getActiveStream:()=>video.srcObject},init:async config=>{events.push('init');assert.equal(config.numOfWorkers,0);await init();video.srcObject=stream;video.dispatchEvent(new Event('loadedmetadata'));},start:()=>events.push('start'),stop:async()=>{events.push('stop');await stop();events.push('stopped');},onProcessed:fn=>handler=fn,offProcessed:fn=>{if(handler===fn)handler=null;}};
  const reads=[],errors=[];
  const session=new BarcodeScannerSession({target:{querySelector:()=>video},preferredScheme:'ean13',load:load||(()=>Promise.resolve(engine)),onReady:()=>events.push('ready'),onRead:value=>{events.push('read');reads.push(value);},onError:message=>errors.push(message)});
  return {session,engine,events,reads,errors,track,video,emit:(value,time)=>{const previous=Date.now;Date.now=()=>time;try{handler?.(value);}finally{Date.now=previous;}}};
}

test('成功時はカメラの停止完了後に一度だけ入力へ通知する',async()=>{
  const stopped=deferred(),f=fixture({stop:()=>stopped.promise});await f.session.start();
  f.emit(result(),100);assert.equal(f.events.includes('stop'),false);
  f.emit(result(),200);f.emit(result(),300);
  assert.ok(f.track.stops>0);assert.equal(f.reads.length,0);
  stopped.resolve();await tick();
  assert.equal(f.reads.length,1);assert.ok(f.events.indexOf('stopped')<f.events.indexOf('read'));
  assert.equal(f.video.srcObject,null);f.session.cancel();await tick();
});

test('利用許可待ちで取り消しても、遅れて取得した映像を停止し、次の読み取りと競合させない',async()=>{
  const permission=deferred(),f=fixture({init:()=>permission.promise});const start=f.session.start();await tick();
  f.session.cancel();const blocked=fixture();await blocked.session.start();assert.equal(blocked.errors.length,1);assert.equal(blocked.events.includes('init'),false);
  permission.resolve();await start;
  assert.ok(f.track.stops>0);assert.equal(f.video.srcObject,null);assert.equal(f.events.includes('start'),false);assert.equal(f.reads.length,0);assert.equal(f.errors.length,0);
  const next=fixture();await next.session.start();assert.ok(next.events.includes('ready'));next.session.cancel();await tick();
});

test('ライブラリ読込中の取消は後続セッションの映像に触れない',async()=>{
  const loading=deferred(),old=fixture({load:()=>loading.promise});const start=old.session.start();old.session.cancel();await tick();
  const next=fixture();await next.session.start();loading.resolve(old.engine);await start;
  assert.equal(old.events.includes('init'),false);assert.equal(next.track.stops,0);
  next.session.cancel();await tick();assert.ok(next.track.stops>0);
});

test('停止中の取消では読み取り値を反映しない',async()=>{
  const stopped=deferred(),f=fixture({stop:()=>stopped.promise});await f.session.start();f.emit(result(),100);f.emit(result(),200);f.session.cancel();stopped.resolve();await tick();assert.equal(f.reads.length,0);
});

test('利用拒否と端末によるカメラ切断から、停止後に再開できる',async()=>{
  const f=fixture({init:async()=>{throw new DOMException('denied','NotAllowedError');}});await f.session.start();assert.equal(f.errors[0],barcodeCameraError({name:'NotAllowedError'}));
  const next=fixture();await next.session.start();next.track.dispatchEvent(new Event('ended'));await tick();
  assert.equal(next.errors.length,1);assert.ok(next.track.stops>0);assert.equal(next.video.srcObject,null);
  const retry=fixture();await retry.session.start();assert.ok(retry.events.includes('ready'));retry.session.cancel();await tick();
});
