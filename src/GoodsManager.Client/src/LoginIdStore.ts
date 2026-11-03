import {api,ApiError,errorText,type ApiClient} from './api';

export type AccountUser={id:string;loginId:string;isAdministrator:boolean;etag:string};

export const loginIdError=(name:string)=>/^[A-Za-z0-9._-]{1,64}$/.test(name)?'':'ログインIDは英数字・ピリオド・アンダースコア・ハイフンの1〜64文字で入力してください。';

export class LoginIdStore{
    current:AccountUser|null=null;
    latest:AccountUser|null=null;
    name='';
    busy=false;
    conflict=false;
    uncertain=false;
    error='';
    notice='';
    private attempted='';
    private version=0;
    private listeners=new Set<()=>void>();

    constructor(public request:ApiClient=api,private onUpdated:(user:AccountUser)=>void=()=>{}){}

    subscribe=(fn:()=>void)=>{
        this.listeners.add(fn);
        return()=>{
            this.listeners.delete(fn);
        };
    };
    snapshot=()=>this.version;
    notify=()=>{
        ++this.version;
        for(const fn of this.listeners)fn();
    };

    get locked(){
        return this.busy||this.uncertain;
    }

    get dirty(){
        return !!this.current&&this.name!==this.current.loginId||this.uncertain;
    }

    update(name:string){
        if(this.locked)return;

        this.name=name;
        this.error='';
        this.notice='';
        this.notify();
    }

    private adopt(user:AccountUser){
        this.current=user;
        this.name=user.loginId;
        this.latest=null;
        this.conflict=false;
        this.uncertain=false;
        this.onUpdated(user);
    }

    async load(){
        if(this.busy)return;

        this.busy=true;
        this.error='';
        this.notice='';
        this.notify();
        try{
            const {user}=await this.request<{user:AccountUser}>('/auth/session');
            if(this.uncertain&&user.loginId===this.attempted){
                this.adopt(user);
                this.notice='ログインIDの変更が保存されていることを確認しました。';
            }
            else if(!this.current||!this.dirty&&!this.conflict)this.adopt(user);
            else{
                this.latest=user;
                this.uncertain=false;
                this.conflict=true;
                this.onUpdated(user);
            }
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
        this.notice='入力を保持しました。最新のログインIDと比較して保存してください。';
        this.notify();
    }

    reset(){
        if(this.locked||!this.current)return;

        this.name=this.current.loginId;
        this.error='';
        this.notify();
    }

    async submit(){
        if(this.locked||this.conflict||!this.current)return;

        const error=loginIdError(this.name);
        if(error){
            this.error=error;
            this.notify();

            return;
        }

        const name=this.name,etag=this.current.etag;
        this.busy=true;
        this.error='';
        this.notice='';
        this.attempted=name;
        this.notify();
        try{
            const result=await this.request<{user:AccountUser}>('/account',{method:'PUT',headers:{'If-Match':etag},body:JSON.stringify({loginId:name})});
            this.adopt(result.user);
            this.notice='ログインIDを変更しました。次回から新しいIDでログインできます。現在のログイン状態は維持します。';
        }
        catch(e){
            if(e instanceof ApiError&&e.status===412)this.conflict=true;

            this.uncertain=!(e instanceof ApiError)||e.status>=500;
            this.error=this.uncertain?'変更結果を確認できませんでした。自動再送はしていません。「最新のログインIDを確認」で結果を確認してください。':errorText(e);
        }
        finally{
            this.busy=false;
            this.notify();
        }
    }
}
