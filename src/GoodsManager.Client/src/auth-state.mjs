/** 初期登録の必要性が判明してからセッションを確認する。判定失敗は呼び出し元へ返す。 */
export async function checkAccess(request) {
  const { setupRequired } = await request('/setup/status');
  if (setupRequired) return { setupRequired: true, session: null };
  try {
    return { setupRequired: false, session: await request('/auth/session') };
  } catch (error) {
    if (error.status === 401) return { setupRequired: false, session: null };
    throw error;
  }
}

/** 認証用ページを復帰先にせず、登録とログインのどちらを案内するかを決める。 */
export function accessDestination(setupRequired, authenticated, pathname) {
  if (setupRequired) return pathname === '/setup' ? null : '/setup';
  if (!authenticated) return pathname === '/login' ? null : '/login';
  return pathname === '/setup' || pathname === '/login' ? 'return' : pathname === '/' ? '/collection' : null;
}
