import {api,ApiError,errorText,type ApiClient} from './api';
import {mutation} from './RegistrationStore';

export type ImageSettings={keepOriginal:boolean;displayMaxEdge:number;displayQuality:number;thumbMaxEdge:number;thumbQuality:number;uploadMaxBytes:number;maxPixels:number;maxEdge:number;conversionTimeoutSeconds:number;workerMemoryBytes:number;temporaryMaxBytes:number;temporaryMaxCount:number;uploadLifetimeHours:number;revision:string;etag:string};

type ImageDraft={keepOriginal:boolean;displayMaxEdge:string;displayQuality:string;thumbMaxEdge:string;thumbQuality:string};

const draftOf=(value:ImageSettings):ImageDraft=>({keepOriginal:value.keepOriginal,displayMaxEdge:String(value.displayMaxEdge),displayQuality:String(value.displayQuality),thumbMaxEdge:String(value.thumbMaxEdge),thumbQuality:String(value.thumbQuality)});

export class ImageSettingsStore{
    current:ImageSettings|null=null;
    latest:ImageSettings|null=null;
    draft:ImageDraft={keepOriginal:false,displayMaxEdge:'2048',displayQuality:'82',thumbMaxEdge:'320',thumbQuality:'75'};
    busy=false;
    conflict=false;
    error='';
    notice='';
    private version=0;
    private listeners=new Set<()=>void>();

    constructor(public request:ApiClient=api){
        this.save=mutation<ImageSettings>(request);
    }

    save;
    subscribe=(listener:()=>void)=>{
        this.listeners.add(listener);
        return()=>{
            this.listeners.delete(listener);
        };
    };
    snapshot=()=>this.version;
    notify=()=>{
        ++this.version;
        for(const listener of this.listeners)listener();
    };

    get locked(){
        return this.busy||this.save.state==='unknown'||this.save.state==='sending';
    }

    get dirty(){
        return !!this.current&&JSON.stringify(this.draft)!==JSON.stringify(draftOf(this.current))||this.save.state==='unknown';
    }

    update<K extends keyof ImageDraft>(key:K,value:ImageDraft[K]){
        if(this.locked)return;

        this.draft={...this.draft,[key]:value};
        this.error='';
        this.notice='';
        this.notify();
    }

    private adopt(value:ImageSettings){
        this.current=value;
        this.draft=draftOf(value);
        this.latest=null;
        this.conflict=false;
    }

    async load(){
        if(this.busy||this.current)return;

        this.busy=true;
        this.error='';
        this.notify();
        try{
            this.adopt(await this.request<ImageSettings>('/settings/images'));
        }catch(e){
            this.error=errorText(e);
        }finally{
            this.busy=false;
            this.notify();
        }
    }

    async fetchLatest(){
        if(this.locked)return;

        this.busy=true;
        this.error='';
        this.notify();
        try{
            this.latest=await this.request<ImageSettings>('/settings/images');
        }catch(e){
            this.error=errorText(e);
        }finally{
            this.busy=false;
            this.notify();
        }
    }

    reviewed(){
        if(this.locked||!this.latest)return;

        this.current=this.latest;
        this.latest=null;
        this.conflict=false;
        this.notice='入力を保持しました。内容を確認して保存してください。';
        this.notify();
    }

    reset(){
        if(this.locked||!this.current)return;

        this.draft=draftOf(this.current);
        this.error='';
        this.notify();
    }

    async submit(){
        if(this.locked||this.conflict||!this.current)return;

        const body={keepOriginal:this.draft.keepOriginal,displayMaxEdge:Number(this.draft.displayMaxEdge),displayQuality:Number(this.draft.displayQuality),thumbMaxEdge:Number(this.draft.thumbMaxEdge),thumbQuality:Number(this.draft.thumbQuality)};
        if(!['displayMaxEdge','displayQuality','thumbMaxEdge','thumbQuality'].every(key=>/^\d+$/.test(this.draft[key as Exclude<keyof ImageDraft,'keepOriginal'>]))||body.displayMaxEdge<1||body.displayMaxEdge>4096||body.thumbMaxEdge<1||body.thumbMaxEdge>1024||body.thumbMaxEdge>body.displayMaxEdge||body.displayQuality<1||body.displayQuality>100||body.thumbQuality<1||body.thumbQuality>100){
            this.error='長辺は表示用1〜4,096px、サムネイル1〜1,024px、品質は1〜100の整数で指定してください。サムネイルの長辺は表示用以下にしてください。';
            this.notify();

            return;
        }

        await this.perform(()=>this.save.begin('/settings/images',body,this.current!.etag,'PUT'));
    }

    async replay(){
        if(!this.busy)await this.perform(()=>this.save.replay());
    }

    private async perform(send:()=>Promise<ImageSettings>){
        this.busy=true;
        this.error='';
        this.notice='';
        this.notify();
        try{
            this.adopt(await send());
            this.notice='画像設定を保存しました。新しくアップロードする画像から適用します。';
        }catch(e){
            if(e instanceof ApiError&&e.status===412)this.conflict=true;

            this.error=this.save.state==='unknown'?'保存結果を確認できませんでした。同じ要求で結果を確認してください。':errorText(e);
        }finally{
            this.busy=false;
            this.notify();
        }
    }
}
