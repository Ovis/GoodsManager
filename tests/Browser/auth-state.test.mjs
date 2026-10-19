import test from 'node:test';
import assert from 'node:assert/strict';
import { checkAccess, accessDestination } from '../../src/GoodsManager.Client/src/auth-state.mjs';
import { safeReturn } from '../../src/GoodsManager.Client/src/collection-state.mjs';

test('アカウント未作成ならセッション取得をせず初期登録を案内する', async () => {
  const paths = [];
  const result = await checkAccess(async path => { paths.push(path); return { setupRequired: true }; });
  assert.deepEqual(paths, ['/setup/status']);
  assert.deepEqual(result, { setupRequired: true, session: null });
  for (const path of ['/', '/collection', '/login', '/products/a']) assert.equal(accessDestination(true, false, path), '/setup');
  assert.equal(accessDestination(true, false, '/setup'), null);
});

test('登録済みなら認証状態に従い、初期登録URLへ戻さない', async () => {
  const session = { user: { loginId: 'owner' } };
  const result = await checkAccess(async path => path === '/setup/status' ? { setupRequired: false } : session);
  assert.deepEqual(result, { setupRequired: false, session });
  assert.equal(accessDestination(false, true, '/setup'), 'return');
  assert.equal(accessDestination(false, true, '/login'), 'return');
  assert.equal(accessDestination(false, true, '/'), '/collection');
  assert.equal(accessDestination(false, true, '/collection'), null);
  assert.equal(safeReturn('/setup'), '/collection');
});

test('登録済みで未ログインなら初期登録URLもログインへ送る', async () => {
  const result = await checkAccess(async path => {
    if (path === '/setup/status') return { setupRequired: false };
    throw { status: 401 };
  });
  assert.deepEqual(result, { setupRequired: false, session: null });
  assert.equal(accessDestination(false, false, '/setup'), '/login');
  assert.equal(accessDestination(false, false, '/collection'), '/login');
  assert.equal(accessDestination(false, false, '/login'), null);
});

test('判定の通信失敗やサーバー障害を未作成・未ログインと誤判定しない', async () => {
  const unavailable = new Error('offline');
  await assert.rejects(checkAccess(async () => { throw unavailable; }), error => error === unavailable);
  for (const status of [403, 500, 503]) {
    const failure = { status };
    await assert.rejects(checkAccess(async path => {
      if (path === '/setup/status') return { setupRequired: false };
      throw failure;
    }), error => error === failure);
  }
});
