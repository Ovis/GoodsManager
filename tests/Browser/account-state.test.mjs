import test from 'node:test';
import assert from 'node:assert/strict';
import { safeReturn } from '../../src/GoodsManager.Client/src/collection-state.mjs';
import { AccountState, passwordError } from '../../src/GoodsManager.Client/src/account-state.mjs';

const entry={id:'credential-1',displayName:'端末',createdAt:'2026-10-27T00:00:00Z',etag:'"v1"'};
function setup(behavior=async()=>undefined,browser={assertion:async()=>({signed:true}),creation:async()=>({created:true})}){
  const calls=[],logins=[];
  const state=new AccountState(async(path,options)=>{
    calls.push({path,...options,body:options?.body?JSON.parse(options.body):undefined});
    if(path==='/account/passkeys'&&!options?.method)return [{...entry}];
    if(path==='/auth/reauthenticate'||path==='/account/passkeys/options')return {challengeId:'challenge',publicKeyOptions:{challenge:'random'}};
    return behavior(path,options);
  },browser,message=>logins.push(message));
  return {state,calls,logins};
}

test('パスワードのコードポイント・確認不一致は送信前に拒否し、成功後だけ再ログインへ進む',async()=>{
  assert.equal(passwordError('😀'.repeat(12),'😀'.repeat(12)),'');
  assert.ok(passwordError('x'.repeat(129),'x'.repeat(129)));
  assert.ok(passwordError('bad\0password-123','bad\0password-123'));
  assert.ok(passwordError('bad\uD800password-123','bad\uD800password-123'));
  const {state,calls,logins}=setup();await state.load();
  await state.changePassword('new-password-123','different','password','current-password-123');
  assert.equal(calls.length,1);assert.equal(logins.length,0);
  await state.changePassword('new-password-123','new-password-123','password','current-password-123');
  assert.deepEqual(calls.at(-1).body,{newPassword:'new-password-123',verification:{password:'current-password-123'}});
  assert.equal(logins.length,1);assert.equal(state.loaded,false);
  assert.equal(JSON.stringify(state).includes('new-password-123'),false);
  assert.equal(JSON.stringify(state).includes('current-password-123'),false);
});

test('パスキー追加は操作に対応した本人確認・登録選択肢・ブラウザ登録を順に行う',async()=>{
  const {state,calls}=setup();await state.load();
  assert.equal(await state.add('新しい端末','passkey',''),true);
  assert.deepEqual(calls.map(x=>x.path),['/account/passkeys','/auth/reauthenticate','/account/passkeys/options','/account/passkeys','/account/passkeys']);
  assert.equal(calls[1].body.purpose,'passkey-add');
  assert.deepEqual(calls[2].body.verification,{challengeId:'challenge',credential:{signed:true}});
  assert.deepEqual(calls[3].body,{challengeId:'challenge',credential:{created:true},displayName:'新しい端末'});
});

test('保存直後の自動取得は成功通知を残し、画面再訪・手動取得で前の通知を消す',async()=>{
  const {state,calls}=setup();await state.load();
  await state.add('新しい端末','password','current-password-123');
  assert.equal(state.notice,'パスキーを登録しました。');
  assert.equal(calls.at(-1).path,'/account/passkeys');assert.equal(calls.at(-1).method,undefined);
  const writes=calls.filter(call=>call.method).length;
  await state.load();
  assert.equal(state.notice,'');
  assert.equal(calls.filter(call=>call.method).length,writes);
});

test('ブラウザのキャンセルと送信中の二重クリックで登録要求を増やさない',async()=>{
  let release;
  const browser={assertion:async()=>({}),creation:()=>new Promise(resolve=>{release=resolve;})};
  const {state,calls}=setup(undefined,browser);await state.load();
  const first=state.add('端末','password','current-password-123');
  while(!release)await new Promise(resolve=>setImmediate(resolve));
  assert.equal(await state.add('別端末','password','current-password-123'),false);
  release({created:true});await first;
  assert.equal(calls.filter(x=>x.path==='/account/passkeys'&&x.method==='POST').length,1);
  browser.creation=async()=>{throw new DOMException('cancel','NotAllowedError');};
  await state.add('別端末','password','current-password-123');
  assert.equal(calls.filter(x=>x.path==='/account/passkeys'&&x.method==='POST').length,1);
  assert.match(state.error,/完了できません/);
});

test('削除は対象の確認と本人確認を必須とし、認証前のETagを保持する',async()=>{
  let state;
  const browser={assertion:async()=>{state.passkeys[0].etag='"changed-by-proof"';return {signed:true};},creation:async()=>({})};
  const fixture=setup(undefined,browser);state=fixture.state;await state.load();state.open('delete',state.passkeys[0]);
  assert.equal(await state.remove('passkey',''),false);
  assert.equal(fixture.calls.length,1);
  state.action.ack=true;await state.remove('passkey','');
  assert.equal(fixture.calls[1].body.purpose,'passkey-delete:credential-1');
  const deleted=fixture.calls.find(x=>x.method==='DELETE');
  assert.equal(deleted.path,'/account/passkeys/credential-1');
  assert.equal(deleted.headers['If-Match'],'"v1"');
  assert.deepEqual(deleted.body,{challengeId:'challenge',credential:{signed:true}});
});

test('412は編集中の名前を保持し、明示的な再取得と見直しまで旧要求を再送しない',async()=>{
  let reject=true;
  const {state,calls}=setup(async()=>{if(reject)throw {status:412,code:'REVISION_MISMATCH'};});
  await state.load();state.open('rename',state.passkeys[0]);state.action.name='編集中';await state.rename();
  assert.equal(state.action.name,'編集中');assert.equal(state.needsRefresh,true);
  assert.equal(await state.rename(),false);
  state.request=async(path,options)=>{calls.push({path,...options});if(!options)return [{...entry,displayName:'別画面の名前',etag:'"v2"'}];};
  await state.load();assert.equal(state.action.original.etag,'"v1"');assert.equal(state.action.latest.displayName,'別画面の名前');
  assert.equal(await state.rename(),false);
  state.review();assert.equal(state.action.original.etag,'"v2"');assert.equal(state.action.name,'編集中');
  reject=false;assert.equal(await state.rename(),true);
  assert.equal(calls.filter(x=>x.method==='PUT').at(-1).headers['If-Match'],'"v2"');
});

test('登録の結果不明では自動再送せず、明示的な最新一覧取得まで別の変更も止める',async()=>{
  const {state,calls}=setup(async()=>{throw new TypeError('connection lost');});
  await state.load();await state.add('端末','password','current-password-123');
  assert.equal(state.uncertain,'add');assert.equal(state.needsRefresh,true);
  assert.equal(await state.add('端末','password','current-password-123'),false);
  assert.equal(calls.filter(x=>x.method==='POST'&&x.path==='/account/passkeys').length,1);
  await state.load();assert.equal(state.needsRefresh,false);assert.equal(state.uncertain,'');
});

test('変更確定後の一覧取得失敗を保存失敗と混同せず、一覧再取得まで変更を止める',async()=>{
  let saved=false;
  const {state}=setup(async()=>{saved=true;});await state.load();state.open('rename',state.passkeys[0]);state.action.name='更新';
  const request=state.request;state.request=async(path,options)=>{if(saved&&!options)throw new TypeError('offline');return request(path,options);};
  assert.equal(await state.rename(),true);
  assert.equal(state.uncertain,'');assert.match(state.notice,/変更しました/);assert.equal(state.needsRefresh,true);
  await state.load();assert.equal(state.notice,'');assert.match(state.error,/完了できません/);
});

test('パスワード変更の結果不明は成功と断定せず、再送せずログインで確認する',async()=>{
  const {state,calls,logins}=setup(async()=>{throw {status:503};});await state.load();
  await state.changePassword('new-password-123','new-password-123','password','current-password-123');
  assert.equal(calls.filter(x=>x.path==='/account/password').length,1);
  assert.equal(logins.length,1);assert.match(logins[0],/結果を確認できません/);assert.doesNotMatch(logins[0],/変更しました/);
  assert.equal(state.loaded,false);assert.equal(await state.changePassword('new-password-123','new-password-123','password','current-password-123'),false);
});

test('逆転した一覧応答が最新のパスキー一覧を上書きしない',async()=>{
  const {state}=setup();let resolveFirst,resolveSecond;
  const pending=[new Promise(resolve=>{resolveFirst=resolve;}),new Promise(resolve=>{resolveSecond=resolve;})];
  state.request=()=>pending.shift();const first=state.load(),second=state.load();
  resolveSecond([{...entry,displayName:'最新'}]);await second;
  resolveFirst([{...entry,displayName:'古い'}]);await first;
  assert.equal(state.passkeys[0].displayName,'最新');
});

test('再ログイン後にアカウント設定へ復帰し、似た別URLや外部URLへは復帰しない',()=>{
  assert.equal(safeReturn('/account'),'/account');
  assert.equal(safeReturn('/account?from=login'),'/account?from=login');
  assert.equal(safeReturn('/account-other'),'/collection');
  assert.equal(safeReturn('https://other.example/account'),'/collection');
});
