import {codeError} from './registration-state.mjs';

/** 認識結果にも入力時と同じ検算を行い、書籍では価格コードなどを受け付けない。 */
export function barcodeValue(result,preferredScheme='ean13'){
  const code=result?.codeResult;
  if(!code||typeof code.code!=='string'||!/^\d+$/.test(code.code))return null;
  let scheme={ean_13:'ean13',ean_8:'ean8',upc_a:'upca'}[code.format],value=code.code;
  if(!scheme)return null;
  if(scheme==='ean13'&&/^(978|979)/.test(value))scheme='isbn13';
  if(preferredScheme.startsWith('isbn')&&scheme!=='isbn13')return null;
  if(scheme==='ean13'&&preferredScheme==='upca'&&value.startsWith('0')){scheme='upca';value=value.slice(1);}
  return codeError(scheme,value)?null:{scheme,value};
}

/** 連続する別フレームで一致した有効値だけを確定し、単発の誤認識を除く。 */
export class StableBarcode{
  key='';count=0;lastTime=0;
  constructor(preferredScheme='ean13'){this.preferredScheme=preferredScheme;}
  accept(result,now=Date.now()){
    const value=barcodeValue(result,this.preferredScheme);
    if(!value){this.key='';this.count=0;this.lastTime=0;return null;}
    const key=value.scheme+':'+value.value;
    this.count=key===this.key&&now>this.lastTime&&now-this.lastTime<=1000?this.count+1:1;
    this.key=key;this.lastTime=now;
    return this.count>=2?value:null;
  }
}

export function barcodeCameraError(error){
  const messages={NotAllowedError:'カメラの利用が許可されていません。ブラウザのサイト設定を確認してください。',SecurityError:'カメラを利用できません。HTTPSとブラウザのサイト設定を確認してください。',NotFoundError:'利用できるカメラが見つかりません。',NotReadableError:'カメラを開始できません。ほかのアプリで使用中の場合は閉じてください。',OverconstrainedError:'このカメラでは読み取りを開始できませんでした。別のブラウザや端末でお試しください。',AbortError:'カメラの開始が中断されました。もう一度お試しください。'};
  return messages[error?.name]||'カメラを開始できませんでした。利用許可を確認して、もう一度お試しください。';
}
