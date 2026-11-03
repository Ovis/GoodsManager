import test from 'node:test';
import assert from 'node:assert/strict';
import {fileURLToPath} from 'node:url';
import {build} from '../../src/GoodsManager.Client/node_modules/vite/dist/node/index.js';
// 本番と同じソースを既存Viteで変換し、HTTP境界だけを差し替える。
const result=await build({configFile:false,root:fileURLToPath(new URL('../../src/GoodsManager.Client/',import.meta.url)),logLevel:'silent',build:{ssr:'src/ProductWorkspace.ts',write:false}});

const code=result.output.find(x=>x.type==='chunk').code;

const {ProductWorkspace}=await import('data:text/javascript;base64,'+Buffer.from(code).toString('base64'));

const id='00000000-0000-0000-0000-000000000001';

const product=()=>({id,name:'商品',revision:'1',category:{id:'category',name:'カテゴリ'},works:[],characters:[],tags:[],identifiers:[],links:[],images:[],primaryImage:null,version:null,memo:null,manufacturer:null,seller:null,totalQuantity:3,inventory:{lots:[{id:'lot',quantity:3,condition:null,storageLocation:'棚',purchasePlace:null,memo:null}],items:[]}});

function workspace(){
    const value=new ProductWorkspace(id);
    value.data=product();

    return value;
}

function transport(t,handler){
    const original=globalThis.fetch;
    const previousWindow=globalThis.window;
    globalThis.window=new EventTarget();
    globalThis.fetch=handler;
    t.after(()=>{
        globalThis.fetch=original;
        globalThis.window=previousWindow;
    });
}

const json=(body,status=200)=>new Response(JSON.stringify(body),{status,headers:{'Content-Type':'application/json'}});

test('商品・画像・所持の編集を開いただけなら確認不要で終了し、同じ商品を再訪しても閲覧状態になる',async()=>{
    for(const area of ['product','images','inventory']){
        const w=workspace();
        w.begin(area);

        assert.equal(w.dirty,true);
        assert.equal(w.hasChanges,false);
        assert.equal(await w.leave(),'left');
        assert.equal(w.mode,'view');
        assert.equal(w.dirty,false);
        assert.equal(w.resources.size,0);
        assert.equal(await w.leave(),'left');
        assert.equal(w.mode,'view');
    }
});

test('商品情報を変えたときは戻る前に確認し、編集継続では保持、破棄では保存せず終了する',async t=>{
    let requests=0;
    transport(t,async()=>{
        requests++;
        return json({});
    });

    const w=workspace();
    w.begin('product');
    w.editor.update('memo','未保存のメモ');

    assert.equal(await w.leave(),'confirm');
    assert.equal(w.mode,'product');
    assert.equal(w.editor.draft.memo,'未保存のメモ');
    assert.equal(w.hasChanges,true);
    assert.equal(await w.leave(true),'left');
    assert.equal(w.mode,'view');
    assert.equal(w.data.memo,null);
    assert.equal(requests,0);

    w.begin('product');

    assert.equal(w.editor.draft.memo,'');

    w.editor.update('memo','変更');
    w.editor.update('memo','');

    assert.equal(w.hasChanges,false);
    assert.equal(await w.leave(),'left');
});

test('画像の並び順・代表の変更と未使用アップロードを破棄確認の対象にする',async()=>{
    const w=workspace();
    w.data.images=[{linkId:'one',isPrimary:true},{linkId:'two',isPrimary:false}];
    w.begin('images');

    assert.equal(w.hasChanges,false);

    w.images.move('two',-1);

    assert.equal(await w.leave(),'confirm');

    w.images.move('two',1);

    assert.equal(w.hasChanges,false);

    w.images.primary='two';

    assert.equal(await w.leave(),'confirm');

    w.images.primary='one';

    assert.equal(w.hasChanges,false);

    const photo={localId:'upload',file:new File(['x'],'test.png'),url:URL.createObjectURL(new Blob(['x'])),key:'key',state:'ready',id:'unused',generation:0};
    w.images.uploads.photos.push(photo);

    let cancelled=0;
    w.images.uploads.cancelPhoto=async p=>{
        cancelled++;
        w.images.uploads.photos=[];
        URL.revokeObjectURL(p.url);
    };

    assert.equal(await w.leave(),'confirm');
    assert.equal(cancelled,0);
    assert.equal(await w.leave(true),'left');
    assert.equal(cancelled,1);
});

test('所持は未反映のロット入力・個体入力・仮編集済み操作のいずれも破棄前に確認する',async()=>{
    const w=workspace();
    w.data.inventory.items=[{id:'item',condition:null,storageLocation:null,purchasePlace:null,memo:null,images:[]}];
    w.begin('inventory');

    const row=w.inventory.lots[0],draft={fields:{...row},quantity:'3',amount:'1'};
    w.lotDrafts.set('lot',draft);

    assert.equal(w.hasChanges,false);

    draft.fields.memo='未反映';

    assert.equal(await w.leave(),'confirm');

    draft.fields.memo=null;

    assert.equal(w.hasChanges,false);

    draft.quantity='';

    assert.equal(w.hasChanges,true);

    draft.quantity='3';
    draft.amount='2';

    assert.equal(w.hasChanges,true);

    draft.amount='1';
    w.beginItem('item');

    assert.equal(w.hasChanges,false);

    w.itemEdit.fields.memo='個体の入力';

    assert.equal(await w.leave(),'confirm');

    w.itemEdit.fields.memo=null;

    assert.equal(w.hasChanges,false);

    w.command({type:'updateLot',lotRef:'lot',quantity:4});

    assert.equal(await w.leave(),'confirm');
    assert.equal(await w.leave(true),'left');
    assert.equal(w.commands.length,0);
    assert.equal(w.data.inventory.lots[0].quantity,3);
});

test('保存中・結果不明・競合・関連作成中は破棄を指定しても離脱せず、入力を保護する',async()=>{
    const w=workspace();
    w.begin('product');
    w.busy=true;

    assert.equal(await w.leave(true),'blocked');

    w.busy=false;
    w.save.state='unknown';

    assert.equal(w.hasChanges,true);
    assert.equal(await w.leave(true),'blocked');

    w.save.state='idle';
    w.conflict={area:'product',latest:null};

    assert.equal(await w.leave(true),'blocked');

    w.conflict=null;
    w.editor.busy=true;

    assert.equal(await w.leave(true),'blocked');

    w.editor.busy=false;

    assert.equal(w.mode,'product');
});

test('未使用画像の取消が失敗したときは戻らず、再試行が成功するまで編集とファイルを保持する',async()=>{
    const w=workspace();
    w.begin('images');

    let fail=true;
    const images=w.images;
    images.cleanup=async()=>{
        if(fail)throw Error('画像の取消に失敗');
    };

    assert.equal(await w.leave(true),'blocked');
    assert.equal(w.mode,'images');
    assert.equal(w.images,images);
    assert.match(w.error,/画像の取消に失敗/);

    fail=false;

    assert.equal(await w.leave(true),'left');
    assert.equal(w.mode,'view');
});

test('未反映のロット入力を保存で黙って捨てず、通信前に止めて保持する',async t=>{
    let requests=0;
    transport(t,async()=>{
        ++requests;
        return json({});
    });

    const w=workspace();
    w.begin('inventory');
    w.lotDrafts.set('lot',{fields:{...w.inventory.lots[0],memo:'未反映'},quantity:'3',amount:'1'});
    await w.submit();

    assert.match(w.error,/未反映/);
    assert.equal(requests,0);
    assert.equal(w.lotDrafts.get('lot').fields.memo,'未反映');
});

test('取消のHTTP待ち中は保存を開始せず、取消後も保存要求を送らない',async t=>{
    let requests=0,finish;
    transport(t,async()=>{
        ++requests;
        return json({});
    });

    const w=workspace();
    w.begin('images');
    w.images.cleanup=()=>new Promise(resolve=>finish=resolve);

    const cancel=w.cancel();

    assert.equal(w.busy,true);

    await w.submit();

    assert.equal(requests,0);

    finish();
    await cancel;

    assert.equal(w.mode,'view');
    assert.equal(w.busy,false);
});

test('同じ個体を繰り返し仮編集しても画像置換は最後に1回、アップロードも一度だけ送る',async t=>{
    const w=workspace();
    let sent;
    transport(t,async(path,options={})=>{
        if(path.endsWith('/auth/csrf'))return json({token:'csrf'});

        if(options.method==='POST'){
            sent=JSON.parse(options.body);
            return json({});
        }

        return json({...product(),revision:'2',inventory:w.inventory});
    });
    w.begin('inventory');
    w.command({type:'extractItems',lotRef:'lot',itemRefs:['new-item'],fields:{}});
    w.beginItem('new-item');

    const photo={localId:'local',file:new File(['x'],'photo.png'),url:URL.createObjectURL(new Blob(['x'])),key:'key',state:'ready',progress:100,id:'upload-once',generation:0};
    w.itemEdit.images.uploads.photos.push(photo);
    w.itemEdit.images.entries.push({ref:'new-image',photo});
    w.itemEdit.fields.memo='一回目';
    w.applyItem();
    w.beginItem('new-item');
    w.itemEdit.fields.memo='二回目';
    w.applyItem();
    await w.submit();

    assert.equal(w.error,'');
    assert.equal(sent.commands.filter(x=>x.type==='replaceItemImages').length,1);
    assert.equal(JSON.stringify(sent).match(/upload-once/g).length,1);
    assert.equal(w.data.inventory.items[0].memo,'二回目');
    assert.equal(w.resources.size,0);
});

test('通信結果不明では入力と固定要求を保ち、別の保存・取消を禁止して同じ要求を再送する',async t=>{
    const w=workspace();
    let fail=true;
    const sent=[];
    transport(t,async(path,options={})=>{
        if(path.endsWith('/auth/csrf'))return json({token:'csrf'});

        if(options.method==='POST'){
            sent.push({body:options.body,headers:options.headers});
            if(fail)throw new TypeError('connection lost');

            return json({});
        }

        return json({...product(),revision:'2'});
    });
    w.begin('inventory');
    w.command({type:'updateLot',lotRef:'lot',quantity:4});
    await w.submit();

    assert.equal(w.save.state,'unknown');
    assert.equal(w.mode,'inventory');
    assert.equal(w.inventory.lots[0].quantity,4);

    await w.cancel();

    assert.equal(w.mode,'inventory');

    await w.submit();

    assert.equal(sent.length,1);

    fail=false;
    await w.replay();

    assert.equal(sent.length,2);
    assert.equal(sent[0].body,sent[1].body);
    assert.equal(sent[0].headers['Idempotency-Key'],sent[1].headers['Idempotency-Key']);
    assert.equal(sent[0].headers['If-Match'],sent[1].headers['If-Match']);
    assert.equal(w.mode,'view');
});

test('412の最新取得だけでは入力・条件を変えず、明示見直しで旧操作を比較用に残す',async t=>{
    const w=workspace();
    let saves=0;
    transport(t,async(path,options={})=>{
        if(path.endsWith('/auth/csrf'))return json({token:'csrf'});

        if(options.method==='POST'){
            ++saves;
            return json({code:'REVISION_MISMATCH'},412);
        }

        return json({...product(),revision:'9'});
    });
    w.begin('inventory');
    w.command({type:'updateLot',lotRef:'lot',quantity:4});

    const original=w.baseEtag;
    await w.submit();

    assert.equal(w.conflict.area,'inventory');

    await w.latest();

    assert.equal(w.inventory.lots[0].quantity,4);
    assert.equal(w.baseEtag,original);

    await w.reviewed();

    assert.equal(w.inventory.lots[0].quantity,3);
    assert.equal(w.previousInventory.lots[0].quantity,4);
    assert.equal(w.previousCommands.length,1);
    assert.equal(w.commands.length,0);
    assert.equal(w.baseEtag,`"product:${id}:9"`);
    assert.equal(saves,1);
});

test('未使用画像の取消が途中失敗しても、先に取消したファイルを再試行可能な形で保持する',async()=>{
    const w=workspace();
    w.begin('images');

    const s=w.images;
    const photos=[0,1].map(n=>({localId:'p'+n,file:new File(['file'+n],'p'+n+'.png'),url:URL.createObjectURL(new Blob(['file'])),key:'key'+n,state:'ready',progress:100,id:'u'+n,generation:0}));
    s.uploads.photos.push(...photos);
    s.entries.push(...photos.map((photo,i)=>({ref:'p'+i,photo})));
    s.uploads.cancelPhoto=async p=>{
        if(p===photos[0]){
            s.uploads.photos=s.uploads.photos.filter(x=>x!==p);
            URL.revokeObjectURL(p.url);
        }else s.uploads.error='cancel failed';
    };
    await assert.rejects(s.cleanup(),/cancel failed/);

    assert.equal(s.uploads.photos.length,2);
    assert.equal(photos[0].state,'cancelled');
    assert.equal(photos[0].id,undefined);
    assert.equal(photos[0].file.name,'p0.png');
    assert.match(photos[0].url,/^blob:/);
    assert.throws(()=>s.serialize());

    for(const p of photos)URL.revokeObjectURL(p.url);
});

test('別の個体画像の取消が失敗したときも、先に取消した編集セッションの選択ファイルを保持する',async()=>{
    const w=workspace();
    w.begin('images');

    const first=w.images,second=new first.constructor();
    w.resources.add(second);

    const photos=[first,second].map((s,n)=>{
        const photo={localId:'p'+n,file:new File(['file'+n],'p'+n+'.png'),url:URL.createObjectURL(new Blob(['file'])),key:'key'+n,state:'ready',progress:100,id:'u'+n,generation:0};
        s.uploads.photos.push(photo);
        s.entries.push({ref:'p'+n,photo});
        s.uploads.cancelPhoto=async p=>{
            if(n===1)s.uploads.error='second cancel failed';else{
                s.uploads.photos=s.uploads.photos.filter(x=>x!==p);
                URL.revokeObjectURL(p.url);
            }
        };

        return photo;
    });
    await w.cancel();

    assert.equal(w.mode,'images');
    assert.match(w.error,/second cancel failed/);
    assert.equal(first.uploads.photos[0],photos[0]);
    assert.equal(photos[0].state,'cancelled');
    assert.equal(photos[0].id,undefined);
    assert.equal(photos[0].file.name,'p0.png');
    assert.match(photos[0].url,/^blob:/);
    assert.throws(()=>first.serialize());

    second.uploads.cancelPhoto=async p=>{
        second.uploads.photos=[];
        URL.revokeObjectURL(p.url);
    };
    await w.cancel();

    assert.equal(w.mode,'view');
    assert.equal(w.resources.size,0);
});
