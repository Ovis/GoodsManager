import {normalize} from './normalize.js';
let engine;
self.onmessage=async({data})=>{
  const {requestId,identity,action}=data;
  const reply=value=>self.postMessage({requestId,identity,...value});
  try{
    if(action==='prepare'){
      const start=performance.now();const {prepare}=await import('./paddle-engine.js');
      engine=await prepare(data.root);reply({done:true,preparationMs:performance.now()-start});
    }else if(action==='normalize'){
      const start=performance.now();const result=await normalize(data.file,2048);reply({done:true,...result,normalizationMs:performance.now()-start});
    }else if(action==='recognize'){
      if(!engine)throw Error('文字読み取りの準備ができていません。');
      const start=performance.now();const result=await engine.recognize(data.blob,2048);reply({done:true,text:result.text,recognitionMs:performance.now()-start});
    }else throw Error('未対応の処理です。');
  }catch{reply({error:true});}
};
