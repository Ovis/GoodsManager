import test from 'node:test';
import assert from 'node:assert/strict';
import { apiSearch, canonicalPage, isFiltered, pageNumbers, safeReturn, updateSearch } from '../../src/GoodsManager.Client/src/collection-state.mjs';
test('確定した条件だけをAPIへ送り、表示形式はURLに保持する',()=>{
 assert.equal(apiSearch('?q=%E6%98%9F&view=table&workIds=a&workIds=b&page=2'),'q=%E6%98%9F&workIds=a&workIds=b&page=2');
});
test('フィルター・並び順・件数変更はpage1へ、表示形式だけはページと確定qを保持する',()=>{
 const original='q=applied&page=3&pageSize=25';
 assert.equal(updateSearch(original,{view:'table'}),'q=applied&page=3&pageSize=25&view=table');
 assert.equal(updateSearch(original,{sort:'quantityDesc'}),'q=applied&pageSize=25&sort=quantityDesc');
 assert.equal(updateSearch(original,{workIds:['a','b']}),'q=applied&pageSize=25&workIds=a&workIds=b');
 assert.equal(updateSearch(original,{pageSize:'100'}),'q=applied&pageSize=100');
});
test('チップ解除は他条件を残し、空の検索語をAPIへ混入させない',()=>{
 assert.equal(updateSearch('q=star&tagIds=a&tagIds=b&page=2',{tagIds:['b']}),'q=star&tagIds=b');
 assert.equal(updateSearch('q=star&page=2&view=table',{q:''}),'view=table');
});
test('件数補正は応答のpageをURLへ反映する',()=>{
 assert.equal(canonicalPage('?q=a&page=9',2),'q=a&page=2');
 assert.equal(canonicalPage('?q=a&page=9',1),'q=a');
 assert.equal(canonicalPage('?pageSize=50',1),'pageSize=50');
});
test('最近登録を表示する範囲とページ番号を絞る',()=>{
 assert.equal(isFiltered('?view=table&page=2'),false);assert.equal(isFiltered('?inventory=positive'),false);
 assert.equal(isFiltered('?inventory=all'),true);assert.equal(isFiltered('?q=a'),true);
 assert.deepEqual(pageNumbers(5,10000),[1,3,4,5,6,7,10000]);assert.deepEqual(pageNumbers(1,0),[]);
});
test('認証後の復帰先はコレクションと商品詳細に限定する',()=>{
 assert.equal(safeReturn('/collection?page=2'),'/collection?page=2');assert.equal(safeReturn('https://evil.example'),'/collection');assert.equal(safeReturn('//evil.example'),'/collection');assert.equal(safeReturn('/collection-other'),'/collection');
});
