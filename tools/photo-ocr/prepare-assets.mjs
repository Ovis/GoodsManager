import fs from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {download} from './fetch.mjs';
const root=fileURLToPath(new URL('../../',import.meta.url));
const client=path.join(root,'src/GoodsManager.Client');
const target=path.join(root,'src/GoodsManager.Web/PhotoOcrAssets');
// A page build adds document/window based import preloading and fails inside a Worker.
const worker=await fs.readFile(path.join(target,'workers/ocr.worker.js'),'utf8');
if(/\b(document|window)\b/.test(worker))throw Error('OCR entry must use the Vite Worker build.');
// Dependency upgrades must also update the reviewed notices, not silently ship
// newly resolved code with an older license inventory.
const inventory=JSON.parse(await fs.readFile(path.join(root,'third-party/photo-ocr/npm-inventory.json'),'utf8'));
const lock=JSON.parse(await fs.readFile(path.join(client,'package-lock.json'),'utf8'));
const remaining=new Set(inventory.map(x=>x.name)),visited=new Set();
async function verify(name){
  if(visited.has(name))return;visited.add(name);
  const pkg=JSON.parse(await fs.readFile(path.join(client,'node_modules',name,'package.json'),'utf8'));
  const reviewed=inventory.find(x=>x.name===name),locked=lock.packages['node_modules/'+name];
  if(!reviewed||reviewed.version!==pkg.version||reviewed.integrity!==locked?.integrity)throw Error('Review OCR dependency license: '+name);
  remaining.delete(name);
  for(const dep of Object.keys(pkg.dependencies||{}))await verify(dep);
}
for(const name of ['@paddleocr/paddleocr-js','@techstark/opencv-js','onnxruntime-web'])await verify(name);
if(remaining.size)throw Error('OCR inventory changed; update notices.');
// worker:false uses our Worker adapter. Do not redistribute the SDK's unused
// prebundled Worker, whose embedded dependency versions differ from our pins.
for(const name of await fs.readdir(path.join(target,'assets')))
  if(/^worker-entry-[\w-]+\.js$/.test(name))await fs.unlink(path.join(target,'assets',name));
await fs.rm(path.join(target,'build-entry.js'),{force:true});
await fs.rm(path.join(target,'workers/worker-entry.js'),{force:true});
const manifest=JSON.parse(await fs.readFile(new URL('./models.json',import.meta.url),'utf8'));
for(const asset of manifest){
  const cached=path.join(root,'artifacts/photo-ocr-models',asset.name);
  await download(asset.url,cached,asset.sha256);
  await fs.mkdir(path.join(target,'models'),{recursive:true});
  await fs.copyFile(cached,path.join(target,'models',asset.name));
}
await fs.mkdir(path.join(target,'runtime/ort'),{recursive:true});
for(const file of await fs.readdir(path.join(client,'node_modules/onnxruntime-web/dist')))
  if(/^ort-wasm-simd-threaded\.(mjs|wasm)$/.test(file))await fs.copyFile(path.join(client,'node_modules/onnxruntime-web/dist',file),path.join(target,'runtime/ort',file));
await fs.cp(path.join(root,'third-party/photo-ocr'),path.join(target,'notices'),{recursive:true});
await fs.copyFile(new URL('./models.json',import.meta.url),path.join(target,'notices/models.json'));
// Browser delivery is distribution too. Keep notices/source availability attached
// to the delivered code without adding a link to the registration screen.
const notices=await fs.readFile(path.join(target,'notices/index.txt'),'utf8');
const eigenSource=notices.match(/^https:\/\/gitlab\.com\/libeigen\/eigen\/[^\s]+\.zip$/m)?.[0];
if(!eigenSource)throw Error('OCR notices must provide the corresponding Eigen source.');
async function attachNotices(directory){
  for(const entry of await fs.readdir(directory,{withFileTypes:true})){
    const file=path.join(directory,entry.name);
    if(entry.isDirectory()){await attachNotices(file);continue;}
    if(!/\.(?:js|mjs)$/.test(entry.name))continue;
    const noticePath=path.relative(directory,path.join(target,'notices/index.txt')).split(path.sep).join('/');
    const banner=`/*! GoodsManager photo OCR: third-party licenses, copyrights and source availability: ${noticePath} (relative to this file).\n * Eigen (MPL-2.0), unmodified corresponding source: ${eigenSource}\n */\n`;
    await fs.writeFile(file,banner+await fs.readFile(file,'utf8'));
  }
}
await attachNotices(target);
console.log('OCR assets prepared; runtime downloads use this app only.');
