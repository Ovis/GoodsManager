import {api,ApiError,errorText,type ApiClient} from './api';
import {mutation} from './RegistrationStore';
import {validText} from './registration-state.mjs';

export type CollectionSpace={id:string;name:string;role:'manager'|'editor';etag:string;revision:string;sampleStatus?:'pending'|'ready'|'failed'|null};
export type CollectionDeleteImpact={id:string;name:string;etag:string;confirmation:string;canDelete:boolean;products:number;trashedProducts:number;lots:number;items:number;images:number;masters:number};

export class CollectionsStore{
    items:CollectionSpace[]=[];
    loading=false;
    loaded=false;
    error='';
    notice='';
    name='';
    importFrom='';
    ack=false;
    rename:{id:string;name:string;etag:string}|null=null;
    conflict=false;
    intent:'create'|'rename'|'delete'|'retry'|null=null;
    deletion:CollectionDeleteImpact|null=null;
    deleteName='';
    reviewing=false;
    private readVersion=0;
    private version=0;
    private listeners=new Set<()=>void>();
    save=mutation<CollectionSpace>();

    constructor(public request:ApiClient=api){
        this.save=mutation<CollectionSpace>(request);
    }

    subscribe=(fn:()=>void)=>{
        this.listeners.add(fn);
        return()=>this.listeners.delete(fn);
    };
    snapshot=()=>this.version;
    notify=()=>{
        this.version++;
        for(const fn of this.listeners)fn();
    };

    get locked(){
        return this.save.state==='sending'||this.save.state==='unknown';
    }

    async load(){
        const version=++this.readVersion;
        this.loading=true;
        this.error='';
        this.notify();
        try{
            const result=await this.request<{items:CollectionSpace[]}>('/collections');
            if(version===this.readVersion){
                const completed=result.items.find(x=>x.sampleStatus==='ready'&&this.items.some(old=>old.id===x.id&&old.sampleStatus==='pending'));
                if(completed)this.notice='「'+completed.name+'」のサンプルを準備できました。';

                this.items=result.items;
                this.loaded=true;
            }
        }catch(e){
            if(version===this.readVersion)this.error=errorText(e);
        }finally{
            if(version===this.readVersion){
                this.loading=false;
                this.notify();
            }
        }
    }

    edit(space:CollectionSpace){
        if(this.locked||space.role!=='manager')return;

        this.rename={id:space.id,name:space.name,etag:space.etag};
        this.conflict=false;
        this.error='';
        this.notify();
    }

    async submit(rename=false){
        if(this.locked)return;

        const name=rename?this.rename?.name:this.name;
        if(!name?.trim()||!validText(name,200)){
            this.error='名前を200コードポイント以内で入力してください。';
            this.notify();

            return;
        }

        if(!rename&&this.importFrom&&this.importFrom!=='@samples'&&!this.ack){
            this.error='設定コピーの内容を確認してください。';
            this.notify();

            return;
        }

        this.intent=rename?'rename':'create';
        this.error='';
        this.notice='';
        try{
            const createBody=this.importFrom==='@samples'?{name,withSamples:true}:{name,importFrom:this.importFrom||null};
            const promise=rename?this.save.begin('/collections/'+this.rename!.id,{name},this.rename!.etag,'PUT'):this.save.begin('/collections',createBody);
            this.notify();
            this.complete(await promise);
        }
        catch(e){
            this.failed(e);
        }finally{
            this.notify();
        }
    }

    async replay(){
        if(this.save.state!=='unknown')return;

        try{
            const promise=this.save.replay();
            this.notify();
            this.complete(await promise);
        }catch(e){
            this.failed(e);
        }finally{
            this.notify();
        }
    }

    private complete(space:CollectionSpace){
        if(this.intent==='delete'){
            this.items=this.items.filter(x=>x.id!==space.id);
            this.deletion=null;
            this.deleteName='';
            if(this.rename?.id===space.id)this.rename=null;

            this.notice='「'+space.name+'」を削除しました。';
            return;
        }

        this.items=this.items.some(x=>x.id===space.id)?this.items.map(x=>x.id===space.id?space:x):this.items.concat(space);
        this.loaded=true;
        if(this.intent==='create'){
            this.name='';
            this.importFrom='';
            this.ack=false;
        }else this.rename=null;

        this.conflict=false;
        this.notice=space.sampleStatus==='pending'?'「'+space.name+'」のサンプルを準備しています。': '「'+space.name+'」を'+(this.intent==='create'?'作成':'変更')+'しました。';
    }

    private failed(error:unknown){
        this.error=this.save.state==='unknown'?'結果を確認できませんでした。同じ要求で結果を確認してください。':errorText(error);
        if(error instanceof ApiError&&error.code==='LAST_COLLECTION_REQUIRED')this.error='最後のコレクションは削除できません。別のコレクションを作成してください。';
        if(error instanceof ApiError&&error.code==='COLLECTION_IMPACT_CHANGED')this.error='確認後にデータが変わりました。削除内容を再取得して確認してください。';
        if(error instanceof ApiError&&error.status===412)this.conflict=true;
    }

    async reviewDelete(space:CollectionSpace){
        if(this.locked||this.reviewing)return;

        this.reviewing=true;
        this.deletion=null;
        this.deleteName='';
        this.error='';
        this.notify();
        try{
            this.deletion=await this.request<CollectionDeleteImpact>('/collections/'+space.id+'/delete-impact');
        }catch(e){
            this.error=errorText(e);
        }finally{
            this.reviewing=false;
            this.notify();
        }
    }

    async remove(){
        const impact=this.deletion;
        if(this.locked||!impact?.canDelete||this.deleteName!==impact.name)return;

        this.intent='delete';
        this.error='';
        this.notice='';
        try{
            const promise=this.save.begin('/collections/'+impact.id,{name:this.deleteName,confirmation:impact.confirmation},impact.etag,'DELETE');
            this.notify();
            this.complete(await promise);
        }catch(e){
            this.failed(e);
            if(e instanceof ApiError&&e.status===412)this.deletion=null;
        }finally{
            this.notify();
        }
    }

    async retrySamples(space:CollectionSpace){
        if(this.locked)return;

        this.intent='retry';
        this.error='';
        try{
            const promise=this.save.begin('/collections/'+space.id+'/samples/retry',{},space.etag);
            this.notify();
            this.complete(await promise);
        }catch(e){
            this.failed(e);
        }finally{
            this.notify();
        }
    }

    async review(){
        await this.load();

        const latest=this.items.find(x=>x.id===this.rename?.id);
        if(latest&&this.rename){
            this.rename.etag=latest.etag;
            this.conflict=false;
            this.notice='最新の名前：'+latest.name+'。入力を確認して保存し直してください。';
            this.notify();
        }
    }
}
