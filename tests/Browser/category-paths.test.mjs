import test from 'node:test';
import assert from 'node:assert/strict';
import {categoryPaths} from '../../src/GoodsManager.Client/src/category-paths.ts';

test('同名の「その他」を親カテゴリの経路で区別する',()=>{
  const paths=categoryPaths([
    {id:'acrylic-other',name:'その他',parentId:'acrylic'},
    {id:'paper-other',name:'その他',parentId:'paper'},
    {id:'acrylic',name:'アクリル・キーホルダー類',parentId:null},
    {id:'paper',name:'紙・印刷物',parentId:null},
    {id:'other',name:'その他',parentId:null}
  ]);
  assert.equal(paths['acrylic-other'],'アクリル・キーホルダー類 › その他');
  assert.equal(paths['paper-other'],'紙・印刷物 › その他');
  assert.equal(paths.other,'その他');
});

test('候補ページ外にある複数階層の祖先も含め、IDと選択対象を維持する',()=>{
  const nodes=[{id:'leaf',name:'その他',parentId:'middle'},{id:'middle',name:'アクリル',parentId:'root'},{id:'root',name:'グッズ',parentId:null}];
  const before=structuredClone(nodes),paths=categoryPaths(nodes);
  assert.deepEqual([{id:'leaf',name:paths.leaf}],[{id:'leaf',name:'グッズ › アクリル › その他'}]);
  assert.deepEqual(nodes,before);
  assert.deepEqual(categoryPaths([]),{});
});

test('不正な循環や欠けた祖先を無限にたどらず検出する',()=>{
  assert.throws(()=>categoryPaths([{id:'a',name:'A',parentId:'b'},{id:'b',name:'B',parentId:'a'}]),/循環/);
  assert.throws(()=>categoryPaths([{id:'a',name:'A',parentId:'missing'}]),/親カテゴリ/);
});
