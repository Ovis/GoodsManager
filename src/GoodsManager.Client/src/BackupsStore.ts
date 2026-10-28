import {api,ApiError,errorText,type ApiClient} from './api';
import {mutation} from './RegistrationStore';
export type BackupSettings={enabled:boolean;localTime:string;timeZone:string;automaticRetention:number;destinationDisplay:string;lastSuccess:string|null;revision:string;etag:string};
export type BackupRun={id:string;kind:string;state:string;startedAt:string;completedAt:string|null;snapshotDataRevision:string|null;byteLength:number|null;errorCode:string|null;downloadable:boolean;retentionManaged:boolean};
export type BackupPage={items:BackupRun[];total:number;page:number;pageSize:number;totalPages:number};
/** 入力と送信済み要求をログイン画面の間も保持し、進捗取得で設定入力を上書きしない */
export class BackupsStore{
 constructor(public request:ApiClient=api){this.save=mutation<BackupSettings|BackupRun|undefined>(request);}
 version=0;listeners=new Set<()=>void>();settings:BackupSettings|null=null;latest:BackupSettings|null=null;enabled=true;time='03:00';conflict=false;data:BackupPage|null=null;detail:BackupRun|null=null;busy=false;loading=false;error='';notice='';query='';private generation=0;
 action:{data:BackupRun;ack:boolean}|null=null;save=mutation<BackupSettings|BackupRun|undefined>();operation:'settings'|'create'|'delete'|null=null;
 subscribe=(fn:()=>void)=>{this.listeners.add(fn);return()=>{this.listeners.delete(fn);};};snapshot=()=>this.version;notify=()=>{++this.version;for(const fn of this.listeners)fn();};
 get locked(){return this.busy||this.save.state==='unknown';}
 get dirty(){return !!this.settings&&(this.enabled!==this.settings.enabled||this.time!==this.settings.localTime)||this.save.state==='unknown';}
 get progressing(){return this.data?.items.some(x=>x.state==='running'||x.state==='deleting')??false;}
 async load(query=this.query){if(query!==this.query)this.data=null;this.query=query;const generation=++this.generation;this.loading=true;this.notify();try{const [page,settings]=await Promise.all([this.request<BackupPage>('/backups'+query),this.request<BackupSettings>('/settings/backups')]);if(generation===this.generation){this.data=page;if(settings&&!this.settings){this.settings=settings;this.enabled=settings.enabled;this.time=settings.localTime;}else if(settings&&this.settings){this.settings={...this.settings,lastSuccess:settings.lastSuccess};}}}catch(e){if(generation===this.generation)this.error=errorText(e);}finally{if(generation===this.generation){this.loading=false;this.notify();}}}
 async fetchLatest(){if(this.locked)return;this.busy=true;this.notify();try{this.latest=await this.request<BackupSettings>('/settings/backups');}catch(e){this.error=errorText(e);}finally{this.busy=false;this.notify();}}
 reviewed(){if(this.locked||!this.latest)return;this.settings=this.latest;this.latest=null;this.conflict=false;this.error='';this.notify();}
 async settingsSave(){if(this.locked||!this.settings||this.conflict)return;if(!/^(?:[01][0-9]|2[0-3]):[0-5][0-9]$/.test(this.time)){this.error='実行時刻を00:00〜23:59で指定してください。';this.notify();return;}this.operation='settings';await this.perform(()=>this.save.begin('/settings/backups',{enabled:this.enabled,localTime:this.time},this.settings!.etag,'PUT'));}
 async create(){if(this.locked||this.action)return;this.operation='create';await this.perform(()=>this.save.begin('/backups',{kind:'manual'}));}
 async openDelete(run:BackupRun){if(this.locked||this.action)return;this.busy=true;this.error='';this.notify();try{const current=await this.request<BackupRun>('/backups/'+run.id);if(current.state==='running'||current.state==='deleted'||current.state==='deleting'){this.error='この状態のバックアップは削除できません。';return;}this.action={data:current,ack:false};}catch(e){this.error=errorText(e);}finally{this.busy=false;this.notify();}}
 close(){if(!this.locked){this.action=null;this.notify();}}
 async remove(){if(this.locked||!this.action?.ack)return;const id=this.action.data.id;this.operation='delete';await this.perform(()=>this.save.begin('/backups/'+id,{},null,'DELETE',{'X-Confirm-Delete':id}));}
 async showDetail(run:BackupRun){try{this.detail=await this.request<BackupRun>('/backups/'+run.id);this.notify();}catch(e){this.error=errorText(e);this.notify();}}
 async perform(send:()=>Promise<BackupSettings|BackupRun|undefined>){this.busy=true;this.error='';this.notice='';this.notify();try{const result=await send();if(this.operation==='settings'){const saved=result as BackupSettings;this.settings=saved;this.enabled=saved.enabled;this.time=saved.localTime;this.conflict=false;this.latest=null;this.notice='バックアップ設定を保存しました。';}else{this.action=null;this.notice=this.operation==='create'?'バックアップを受け付けました。履歴で進捗を確認できます。':'削除を受け付けました。ファイルの整理状況は履歴に表示します。';}await this.load();}catch(e){this.error=e instanceof ApiError&&e.code==='BACKUP_IN_PROGRESS'?'バックアップを実行中です。履歴の完了を待ってください。':errorText(e);if(e instanceof ApiError&&e.status===412)this.conflict=true;}finally{this.busy=false;this.notify();}}
 async replay(){if(!this.busy)await this.perform(()=>this.save.replay());}
}
