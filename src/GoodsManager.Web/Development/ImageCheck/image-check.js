import { MasterRequests } from '/dev/master-check/requests.mjs';
const $=id=>document.getElementById(id), show=(id,x)=>{$(id).textContent=typeof x==='string'?x:JSON.stringify(x,null,2);};
const logs=[]; function log(x){logs.push(new Date().toLocaleTimeString()+' '+x);show('result',logs.slice(-40).join('\n'));}
const api=new MasterRequests({record:log});
let authenticated=false,busy=false,settings=null,product=null,entries=[],primaryRef=null,lastUpload=null,uploadState=null,xhr=null,pollVersion=0,preparing=false;
function render(){
  $('controls').disabled=!authenticated||busy; $('settings-save').disabled=!settings||api.status==='unknown'; $('product-save').disabled=!product||api.status==='unknown';
  $('replay').disabled=busy||!authenticated||!api.last; $('upload').disabled=!!xhr; $('upload-replay').disabled=!!xhr||!lastUpload; $('abort').disabled=!xhr;
  $('cancel').disabled=!uploadState||['consumed','cancelled'].includes(uploadState.state); $('append').disabled=uploadState?.state!=='ready'||!product;
  show('request',api.last?{...api.last,status:api.status}:'変更要求なし');
}
async function run(action){if(busy)return;busy=true;render();try{await action();}catch(e){log(e.code?`HTTP ${e.status} / ${e.code} / traceId=${e.traceId}`:e.message);show('response',{message:e.message,code:e.code,errors:e.errors,traceId:e.traceId});log('入力を保持しています。自動再送はしていません。');}finally{busy=false;render();}}
function adoptSettings(x){settings=x;show('settings',x);$('keep-original').checked=x.keepOriginal;for(const [id,key] of [['display-edge','displayMaxEdge'],['display-quality','displayQuality'],['thumb-edge','thumbMaxEdge'],['thumb-quality','thumbQuality']])$(id).value=x[key];}
function reference(x){return x.linkId??x.clientRef;}
function imageRows(){
  $('entries').replaceChildren();
  for(const [index,entry] of entries.entries()){
    const row=document.createElement('div');row.className='row';const image=document.createElement('img');image.width=120;image.alt='商品画像';image.src=entry.thumbnailUrl??entry.previewUrl;row.append(image);
    const text=document.createElement('p');text.textContent=`${reference(entry)} / 並び順 ${entry.sortOrder}${primaryRef===reference(entry)?' / 代表':''}`;row.append(text);
    const button=(title,action)=>{const b=document.createElement('button');b.textContent=title;b.type='button';b.onclick=()=>{action();imageRows();};row.append(b);};
    button('代表にする',()=>primaryRef=reference(entry));
    button('上へ',()=>{if(index>0){[entries[index-1],entries[index]]=[entries[index],entries[index-1]];entries.forEach((x,i)=>x.sortOrder=i);}});
    button('下へ',()=>{if(index<entries.length-1){[entries[index+1],entries[index]]=[entries[index],entries[index+1]];entries.forEach((x,i)=>x.sortOrder=i);}});
    button('現在のアップロードで差替え',()=>{if(uploadState?.state!=='ready'){log('先に別の画像をアップロードしてください。');return;}if(entry.linkId)entry.replacementUploadId=uploadState.id;else entry.temporaryUploadId=uploadState.id;entry.previewUrl=uploadState.previewUrl;entry.thumbnailUrl=uploadState.previewUrl;});
    button('解除',()=>{entries.splice(index,1);if(primaryRef===reference(entry))primaryRef=entries.length?reference(entries[0]):null;});$('entries').append(row);
  }
}
function adoptProduct(x){product={id:$('product-id').value.trim(),etag:x.etag};entries=x.entries.map(e=>({...e}));primaryRef=entries.find(e=>e.isPrimary)?.linkId??null;show('product-state',x);imageRows();}
async function session(){try{const s=await api.connect();authenticated=true;show('session',s);adoptSettings(await api.read('settings/images'));}catch{authenticated=false;show('session','ログインしてから取得してください。');}}
async function poll(id,version){
  if(version!==pollVersion)return;
  try{const next=await api.read(`temporary-uploads/${id}`);if(version!==pollVersion)return;uploadState=next;show('upload-state',uploadState);$('preview').hidden=uploadState.state!=='ready';if(uploadState.state==='ready')$('preview').src=uploadState.previewUrl;render();if(['pending','processing'].includes(uploadState.state))setTimeout(()=>poll(id,version),1000);}catch(e){log('状態の取得に失敗しました。再送操作で照合できます。');}
}
async function sendUpload(replay=false){
  if(xhr||preparing)return;if(!replay){const file=$('file').files[0];if(!file){log('ファイルを選んでください。');return;}lastUpload={file,key:crypto.randomUUID(),sentAt:Date.now(),context:api.context};}
  if(Date.now()-lastUpload.sentAt>=7*86400000){log('7日経過したため同じキーでは再送しません。');return;}
  preparing=true;render();
  try {
  const csrf=await api.read('auth/csrf'); const form=new FormData();form.append('file',lastUpload.file);
  uploadState=null;++pollVersion;$('preview').hidden=true;$('progress').value=0;
  await new Promise(resolve=>{
    xhr=new XMLHttpRequest();xhr.open('POST','/api/v1/temporary-uploads');xhr.setRequestHeader('X-CSRF-Token',csrf.token);xhr.setRequestHeader('Client-Upload-Key',lastUpload.key);for(const [key,value] of Object.entries(api.contextHeaders(lastUpload.context)))xhr.setRequestHeader(key,value);
    xhr.upload.onprogress=e=>{if(e.lengthComputable)$('progress').value=Math.round(e.loaded/e.total*100);};
    xhr.onload=()=>{const status=xhr.status;let body;try{body=JSON.parse(xhr.responseText);}catch{body={};}log(`POST /api/v1/temporary-uploads → ${status} / key=${lastUpload.key}`);show('upload-state',body);if(status===202){uploadState=body;poll(body.id,++pollVersion);}else show('response',body);};
    xhr.onerror=()=>show('upload-state','通信失敗・結果不明。同じファイル・キーで照合してください。');xhr.onabort=()=>show('upload-state','送信を中断しました。結果不明の場合は同じファイル・キーで照合できます。');
    xhr.onloadend=()=>{xhr=null;render();resolve();};render();xhr.send(form);
  });
  } finally {preparing=false;render();}
}
$('session-refresh').onclick=()=>run(session);$('settings-get').onclick=()=>run(async()=>adoptSettings(await api.read('settings/images')));
$('settings-save').onclick=()=>run(async()=>{const body={keepOriginal:$('keep-original').checked,displayMaxEdge:Number($('display-edge').value),displayQuality:Number($('display-quality').value),thumbMaxEdge:Number($('thumb-edge').value),thumbQuality:Number($('thumb-quality').value)};const x=await api.write('settings/images','PUT',body,settings.etag);adoptSettings(x);show('response',x);});
$('upload').onclick=()=>sendUpload().catch(e=>log(e.message));$('upload-replay').onclick=()=>sendUpload(true).catch(e=>log(e.message));$('abort').onclick=()=>xhr?.abort();
$('cancel').onclick=()=>run(async()=>{const csrf=await api.read('auth/csrf');const response=await fetch(`/api/v1/temporary-uploads/${uploadState.id}`,{method:'DELETE',headers:{...api.contextHeaders(),'X-CSRF-Token':csrf.token},credentials:'same-origin',cache:'no-store'});log(`DELETE upload → ${response.status}`);if(!response.ok){show('response',await response.json());return;}await poll(uploadState.id,++pollVersion);});
$('append').onclick=()=>{if(uploadState?.state!=='ready'||!product)return;const entry={clientRef:crypto.randomUUID().replaceAll('-','').slice(0,12),temporaryUploadId:uploadState.id,sortOrder:entries.length,previewUrl:uploadState.previewUrl};entries.push(entry);primaryRef??=entry.clientRef;imageRows();};
$('product-get').onclick=()=>run(async()=>{const id=$('product-id').value.trim();if(!/^[0-9a-f-]{36}$/i.test(id))throw new Error('商品IDを確認してください。');adoptProduct(await api.read(`products/${id}/images`));});
$('product-save').onclick=()=>run(async()=>{const body={entries:entries.map(e=>e.linkId?{linkId:e.linkId,sortOrder:e.sortOrder,...(e.replacementUploadId?{replacementUploadId:e.replacementUploadId}:{})}:{clientRef:e.clientRef,temporaryUploadId:e.temporaryUploadId,sortOrder:e.sortOrder}),primaryRef};const x=await api.write(`products/${product.id}/images`,'PUT',body,product.etag);adoptProduct(x);show('response',x);if(uploadState)await poll(uploadState.id,++pollVersion);});
$('replay').onclick=()=>run(async()=>{show('response',await api.replay());log('再送応答です。次の編集前に商品画像または設定を再取得してください。');});
await run(session);
