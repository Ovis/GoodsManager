import test from 'node:test';
import assert from 'node:assert/strict';
import {fileURLToPath} from 'node:url';
import {build} from '../../src/GoodsManager.Client/node_modules/vite/dist/node/index.js';

const result=await build({configFile:false,root:fileURLToPath(new URL('../../src/GoodsManager.Client/',import.meta.url)),logLevel:'silent',build:{ssr:'src/GuardedHistory.ts',write:false}});

const {GuardedHistory}=await import('data:text/javascript;base64,'+Buffer.from(result.output.find(x=>x.type==='chunk').code).toString('base64'));

const turn=()=>new Promise(resolve=>setImmediate(resolve));

function deferred(){
    let resolve;
    const promise=new Promise(done=>resolve=done);

    return {promise,resolve};
}

function fixture(){
    let index=0;
    const entries=[{url:'/collection',state:{key:'list',scroll:240,draftQ:'検索語'}}],changes=[],moves=[],scrolls=[];
    let router;
    const port={read:()=>structuredClone(entries[index]),replace:entry=>{
            entries[index]=structuredClone(entry);
        },push:entry=>{
            entries.splice(++index);
            entries.push(structuredClone(entry));
        },go:delta=>{
            moves.push(delta);
            queueMicrotask(()=>{
                index+=delta;

                assert.ok(index>=0&&index<entries.length);

                void router.pop();
            });
        },changed:()=>changes.push(entries[index].url),restoreScroll:value=>scrolls.push(value)};
    router=new GuardedHistory(port);

    return {router,entries,changes,moves,scrolls,get index(){
            return index;
        },get current(){
            return entries[index];
        },async visit(url,state={}){
            await router.navigate(url,state);
        },go(delta){
            index+=delta;
            void router.pop();
        }};
}

test('ガードなしの移動は同期反映し、置換と戻る・進むで検索・スクロール状態を維持する',async()=>{
    const f=fixture();
    const pending=f.router.navigate('/product',{key:'product',scroll:0});

    assert.equal(f.current.url,'/product');

    await pending;
    await f.router.navigate('/product?detail=1',{key:'replaced'},true);

    assert.equal(f.entries.length,2);

    f.go(-1);
    await turn();

    assert.equal(f.current.state.draftQ,'検索語');
    assert.equal(f.current.state.scroll,240);

    f.go(1);
    await turn();

    assert.equal(f.current.url,'/product?detail=1');
});

test('サイドメニューの移動を取り消すとURLと履歴を変えず、許可した移動だけを1件追加する',async()=>{
    const f=fixture();
    await f.visit('/product');

    let allowed=false,calls=0;
    f.router.register({blocks:()=>true,approve:async()=>{
            calls++;
            return allowed;
        }});
    await f.router.navigate('/settings',{key:'settings'});

    assert.equal(f.current.url,'/product');
    assert.equal(f.entries.length,2);

    allowed=true;
    await f.router.navigate('/settings',{key:'settings'});

    assert.equal(f.current.url,'/settings');
    assert.equal(f.entries.length,3);
    assert.equal(calls,2);
});

test('ブラウザで戻って編集を続けると履歴位置を復元し、次の戻る・進むも同じ項目を辿る',async()=>{
    const f=fixture();
    await f.visit('/product',{key:'product',scroll:320});

    const gate=deferred();
    let asks=0;
    const stop=f.router.register({blocks:()=>true,approve:()=>{
            asks++;
            return gate.promise;
        }});
    f.go(-1);
    await turn();

    assert.equal(f.index,1);
    assert.equal(f.current.url,'/product');
    assert.equal(asks,1);
    assert.deepEqual(f.moves,[1]);

    gate.resolve(false);
    await turn();

    assert.equal(f.entries.length,2);
    assert.deepEqual(f.changes,['/product']);
    assert.deepEqual(f.scrolls,[320]);

    stop();
    f.go(-1);
    await turn();

    assert.equal(f.index,0);

    f.go(1);
    await turn();

    assert.equal(f.index,1);
    assert.equal(f.current.url,'/product');
});

test('戻る時に破棄を許可すると元の履歴へ移り、一覧条件と進む履歴を壊さない',async()=>{
    const f=fixture();
    await f.visit('/product');
    f.router.register({blocks:()=>true,approve:async()=>true});
    f.go(-1);
    await turn();

    assert.equal(f.index,0);
    assert.equal(f.current.url,'/collection');
    assert.equal(f.current.state.scroll,240);
    assert.equal(f.current.state.draftQ,'検索語');
    assert.equal(f.entries.length,2);
    assert.deepEqual(f.moves,[1,-1]);
    assert.deepEqual(f.changes,['/product','/collection']);
});

test('進むと複数項目の戻るでも、確認は元の位置で1回だけ行い同じ距離を再実行する',async()=>{
    const f=fixture();
    await f.visit('/product');
    await f.visit('/settings');
    f.go(-1);
    await turn();

    let asks=0;
    f.router.register({blocks:()=>true,approve:async()=>{
            asks++;
            return true;
        }});
    f.go(1);
    await turn();

    assert.equal(f.current.url,'/settings');
    assert.deepEqual(f.moves,[-1,1]);

    f.go(-2);
    await turn();

    assert.equal(f.current.url,'/collection');
    assert.deepEqual(f.moves,[-1,1,2,-2]);
    assert.equal(asks,2);
    assert.equal(f.entries.length,3);
});

test('確認中に戻るを繰り返しても確認を増やさず、編集継続後の履歴位置を維持する',async()=>{
    const f=fixture();
    await f.visit('/product');

    const gate=deferred();
    let asks=0;
    f.router.register({blocks:()=>true,approve:()=>{
            asks++;
            return gate.promise;
        }});
    f.go(-1);
    await turn();
    f.go(-1);
    await turn();

    assert.equal(asks,1);
    assert.equal(f.index,1);

    gate.resolve(false);
    await turn();

    assert.equal(f.index,1);
    assert.equal(f.entries.length,2);
    assert.deepEqual(f.changes,['/product']);
});

test('サイドメニューの確認中に戻るを押しても最初の移動先を維持する',async()=>{
    const f=fixture();
    await f.visit('/product');

    const gate=deferred();
    let asks=0;
    f.router.register({blocks:()=>true,approve:()=>{
            asks++;
            return gate.promise;
        }});

    const pending=f.router.navigate('/settings',{});
    f.go(-1);
    await turn();

    assert.equal(f.index,1);

    gate.resolve(true);
    await pending;

    assert.equal(asks,1);
    assert.equal(f.current.url,'/settings');
    assert.equal(f.entries.length,3);
    assert.deepEqual(f.changes,['/product','/settings']);
});

test('保存処理や画像取消が離脱を拒否・失敗した場合は元の画面を保つ',async()=>{
    const f=fixture();
    await f.visit('/product');
    f.router.register({blocks:()=>true,approve:async()=>{
            throw Error('cancel failed');
        }});
    f.go(-1);
    await turn();

    assert.equal(f.current.url,'/product');
    assert.equal(f.index,1);
    assert.equal(f.entries.length,2);
});

test('認証切れへの移動や画面の解除後に、遅れて確定した古い確認を実行しない',async()=>{
    const f=fixture();
    await f.visit('/product');

    const gate=deferred();
    const stop=f.router.register({blocks:()=>true,approve:()=>gate.promise});
    const pending=f.router.navigate('/settings',{});
    await f.router.navigate('/login',{},false,true);
    stop();
    gate.resolve(true);
    await pending;

    assert.equal(f.current.url,'/login');
    assert.deepEqual(f.changes,['/product','/login']);
});
