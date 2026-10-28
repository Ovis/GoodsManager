import type {QuaggaJSStatic,QuaggaJSResultObject} from '@ericblade/quagga2';
import {StableBarcode,barcodeCameraError} from './barcode-result.mjs';
export type ScannedBarcode={scheme:string;value:string};
type Options={target:HTMLElement;preferredScheme:string;onRead:(value:ScannedBarcode)=>void;onError:(message:string)=>void;onReady:()=>void;load?:()=>Promise<QuaggaJSStatic>};
// Quaggaのカメラ状態は共通なので、終了処理と次の初期化を重ねない。
let owner:BarcodeScannerSession|null=null;
export class BarcodeScannerSession{
  private engine:QuaggaJSStatic|null=null;
  private closed=false;private cancelled=false;private initializing=false;
  private cleanupPromise:Promise<void>|null=null;
  private video:HTMLVideoElement|null=null;
  private stable:StableBarcode;
  private endedTracks:MediaStreamTrack[]=[];
  constructor(private options:Options){this.stable=new StableBarcode(options.preferredScheme);}
  private stopTracks=()=>{
    const streams=[this.video?.srcObject,this.engine?.CameraAccess.getActiveStream()];
    for(const stream of streams)if(stream&&'getTracks' in stream)for(const track of stream.getTracks())track.stop();
  };
  private lateStream=()=>{if(this.closed)this.stopTracks();};
  private ended=()=>{if(!this.closed)void this.fail('カメラが切断されました。もう一度お試しください。');};
  private processed=(result:QuaggaJSResultObject)=>{
    if(this.closed)return;
    const value=this.stable.accept(result);
    if(!value)return;
    this.closed=true;
    void this.cleanup().then(()=>{if(!this.cancelled)this.options.onRead(value);});
  };
  async start(){
    if(this.closed)return;
    if(owner){this.closed=true;this.options.onError('前回のカメラの終了を待っています。ブラウザの利用許可の確認を閉じてから、もう一度お試しください。');return;}
    owner=this;
    try{
      const engine=await (this.options.load?.()??import('@ericblade/quagga2').then(module=>module.default));
      if(this.closed)return;
      this.engine=engine;this.video=this.options.target.querySelector('video');
      this.video?.addEventListener('loadedmetadata',this.lateStream);
      this.initializing=true;
      await engine.init({inputStream:{type:'LiveStream',target:this.options.target,constraints:{facingMode:'environment',width:{ideal:1280},height:{ideal:720}},size:1280},numOfWorkers:0,frequency:10,locate:true,locator:{patchSize:'medium',halfSample:true},canvas:{createOverlay:false},decoder:{readers:['upc_reader','ean_reader','ean_8_reader']}});
      this.initializing=false;
      if(this.closed)return;
      this.endedTracks=engine.CameraAccess.getActiveStream()?.getTracks()||[];
      for(const track of this.endedTracks)track.addEventListener('ended',this.ended,{once:true});
      engine.onProcessed(this.processed);engine.start();this.options.onReady();
    }catch(error){if(!this.cancelled){this.closed=true;await this.cleanup();this.options.onError(barcodeCameraError(error));}}
    finally{this.initializing=false;if(this.closed)await this.cleanup();}
  }
  private async fail(message:string){this.closed=true;await this.cleanup();if(!this.cancelled)this.options.onError(message);}
  cancel(){
    this.cancelled=true;this.closed=true;
    // 許可待ちのPromiseは中断できないため、遅れて取得した映像も停止する。
    this.stopTracks();
    if(!this.initializing)void this.cleanup();
  }
  private cleanup():Promise<void>{
    if(this.cleanupPromise)return this.cleanupPromise;
    this.cleanupPromise=(async()=>{
      try{
        this.stopTracks();
        this.engine?.offProcessed(this.processed);
        for(const track of this.endedTracks)track.removeEventListener('ended',this.ended);
        if(this.engine)await this.engine.stop();
      }catch{/* 停止処理が失敗しても、自分の映像トラックを停止して所有権を解放する。 */}
      finally{
        this.stopTracks();this.video?.removeEventListener('loadedmetadata',this.lateStream);
        if(this.video){this.video.pause();this.video.srcObject=null;}
        if(owner===this)owner=null;
      }
    })();
    return this.cleanupPromise;
  }
}
