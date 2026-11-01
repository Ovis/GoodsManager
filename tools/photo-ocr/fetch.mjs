import fs from 'node:fs/promises';
import {createHash} from 'node:crypto';
import path from 'node:path';
export async function download(url,destination,sha256){
  let bytes;
  try{bytes=await fs.readFile(destination);}catch{}
  if(bytes&&createHash('sha256').update(bytes).digest('hex')===sha256)return bytes;
  const response=await fetch(url,{signal:AbortSignal.timeout(180000)});
  if(!response.ok)throw Error('Download failed: '+response.status+' '+url);
  bytes=Buffer.from(await response.arrayBuffer());
  if(createHash('sha256').update(bytes).digest('hex')!==sha256)throw Error('Checksum mismatch: '+url);
  await fs.mkdir(path.dirname(destination),{recursive:true});
  await fs.writeFile(destination,bytes);
  return bytes;
}
