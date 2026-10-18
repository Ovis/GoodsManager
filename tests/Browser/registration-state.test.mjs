import test from 'node:test';
import assert from 'node:assert/strict';
import {FrozenMutation} from '../../src/GoodsManager.Client/src/frozen-mutation.ts';
import {blankDraft,nextDraft,carryKeys,codeError,validText,LatestCheck,duplicateInput,productInput} from '../../src/GoodsManager.Client/src/registration-state.mjs';
import {safeReturn} from '../../src/GoodsManager.Client/src/collection-state.mjs';
test('次へは承認した6項目だけをコピーし、数量・商品属性・写真以外の入力を初期化する',()=>{
 const draft={...blankDraft(),name:'名前',quantity:'7',categoryId:'cat',workIds:['w'],characterIds:['c'],tagIds:['t'],condition:'opened',storageLocation:'棚',purchasePlace:'店',ownershipMemo:'所持',memo:'商品',version:'初回',manufacturerId:'m',sellerId:'s',identifiers:[{scheme:'ean13',value:'9780306406157'}],links:['https://example.com']};
 const next=nextDraft(draft,true,carryKeys);for(const key of carryKeys)assert.deepEqual(next[key],draft[key]);assert.equal(next.quantity,'1');assert.equal(next.name,'');assert.deepEqual(next.characterIds,[]);assert.equal(next.memo,'');assert.equal(next.ownershipMemo,'');assert.equal(next.version,'');assert.equal(next.manufacturerId,'');assert.deepEqual(next.identifiers,blankDraft().identifiers);assert.deepEqual(next.links,['']);next.workIds.push('other');assert.deepEqual(draft.workIds,['w']);
 assert.deepEqual(nextDraft(draft,false,carryKeys),blankDraft());assert.equal(nextDraft(draft,true,['categoryId','name']).name,'');
});
test('コードの種類・桁数・チェックディジットを検証し、入力表記は保存まで保持する',()=>{
 for(const [scheme,value] of [['ean13','９７８-０３０６４０６１５７'],['ean8','96385074'],['upca','036000291452'],['isbn10','0-306-40615-2'],['isbn13','9780306406157']])assert.equal(codeError(scheme,value),'');
 for(const [scheme,value] of [['ean13','9780306406158'],['isbn13','4006381333931'],['isbn10','0-306-40615-3'],['ean8','123'],['ean13','  ']])assert.ok(codeError(scheme,value));
 const draft={...blankDraft(),identifiers:[{scheme:'isbn10',value:'0-306-40615-2'},{scheme:'ean13',value:''}]};assert.deepEqual(duplicateInput(draft).identifiers,[{scheme:'isbn10',value:'0-306-40615-2'}]);assert.deepEqual(productInput(draft).identifiers,duplicateInput(draft).identifiers);
});
test('コードポイント上限を判定し、絵文字は1、孤立サロゲートとNULは拒否する',()=>{assert.equal(validText('😀'.repeat(200),200),true);assert.equal(validText('😀'.repeat(201),200),false);assert.equal(validText('\ud800',200),false);assert.equal(validText('x\0',200),false);});
test('候補照会は遅れて返った旧応答と保存直前の応答を採用しない',async()=>{
 const gate=new LatestCheck(),accepted=[];let first,second;const a=gate.run('old',()=>new Promise(resolve=>first=resolve),x=>accepted.push(x));const b=gate.run('new',()=>new Promise(resolve=>second=resolve),x=>accepted.push(x));second('new result');await b;first('old result');await a;assert.deepEqual(accepted,['new result']);let pending;const c=gate.run('last',()=>new Promise(resolve=>pending=resolve),x=>accepted.push(x));gate.invalidate();pending('after save');await c;assert.deepEqual(accepted,['new result']);
});
test('保存結果不明では別保存を禁止し、再送は本文・キー・If-Matchを保持してCSRFだけ再取得する',async()=>{
 const sent=[];let csrf=0,fail=true;const m=new FrozenMutation({key:()=> 'key',now:()=>10,csrf:async()=>`csrf-${++csrf}`,post:async(r,c)=>{sent.push({r,c});if(fail)throw new TypeError('offline');return {id:'saved'};}});
 const draft={quantity:2};await assert.rejects(m.begin('/products/id/holdings',draft,'"product:id:3"'));assert.equal(m.state,'unknown');draft.quantity=9;assert.throws(()=>m.begin('/products',{}));fail=false;assert.deepEqual(await m.replay(),{id:'saved'});assert.strictEqual(sent[0].r,sent[1].r);assert.equal(sent[1].r.body,'{"quantity":2}');assert.equal(sent[1].r.etag,'"product:id:3"');assert.equal(sent[1].r.key,'key');assert.notEqual(sent[0].c,sent[1].c);
});
test('サーバー障害や認証切れ後も不明結果を保持し、確定した競合応答で新しい確認へ移れる',async()=>{
 let mode='server',auth=true;const m=new FrozenMutation({csrf:async()=>{if(!auth)throw {status:401};return 'csrf';},post:async()=>{if(mode==='server')throw {status:500};if(mode==='conflict')throw {status:409};return {};}});
 await assert.rejects(m.begin('/products',{}));assert.equal(m.state,'unknown');auth=false;await assert.rejects(m.replay());assert.equal(m.state,'unknown');auth=true;mode='conflict';await assert.rejects(m.replay());assert.equal(m.state,'failed');mode='ok';await m.begin('/products',{});assert.equal(m.state,'success');
});
test('送信中の二重クリックと7日を過ぎた再送を禁止する',async()=>{
 let done;let now=1;const m=new FrozenMutation({now:()=>now,csrf:async()=> 'csrf',post:()=>new Promise(resolve=>done=resolve)});const pending=m.begin('/products',{});assert.throws(()=>m.begin('/products',{}));assert.throws(()=>m.replay());await Promise.resolve();done({});await pending;now+=7*86400000;assert.throws(()=>m.replay());
});
test('CSRF取得前の失敗では別保存が可能で、ログイン後の復帰先に登録画面を認める',async()=>{
 const m=new FrozenMutation({csrf:async()=>{throw {status:401};},post:async()=>{throw new Error('must not send');}});await assert.rejects(m.begin('/products',{}));assert.equal(m.state,'failed');assert.equal(safeReturn('/register'),'/register');assert.equal(safeReturn('/register-other'),'/collection');assert.equal(safeReturn('/register?redirect=https://example.com'),'/collection');
});
