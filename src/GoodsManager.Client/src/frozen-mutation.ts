export type FrozenRequest = Readonly<{path:string;method:string;body:string;etag:string|null;key:string;sentAt:number;headers:Readonly<Record<string,string>>}>;
export type MutationState = 'none'|'sending'|'success'|'failed'|'unknown';
/** 送信内容をページ内に保持し、結果不明の間は別の保存要求へ切り替えない */
export class FrozenMutation<T> {
  last:FrozenRequest|null=null;
  state:MutationState='none';
  private options:{csrf:()=>Promise<string>;post:(request:FrozenRequest,csrf:string)=>Promise<T>;key:()=>string;now:()=>number};
  constructor(options:{csrf:()=>Promise<string>;post:(request:FrozenRequest,csrf:string)=>Promise<T>;key?:()=>string;now?:()=>number}){
    this.options={...options,key:options.key??(()=>crypto.randomUUID()),now:options.now??(()=>Date.now())};
  }
  begin(path:string,body:unknown,etag:string|null=null,method='POST',headers:Record<string,string>={}){
    if(this.state==='unknown'||this.state==='sending')throw new Error('前の保存結果を先に確認してください。');
    this.last=Object.freeze({path,method,body:JSON.stringify(body),etag,key:this.options.key(),sentAt:this.options.now(),headers:Object.freeze({...headers})});
    return this.send();
  }
  replay(){
    if(!this.last||this.state==='sending')throw new Error('再送できる要求がありません。');
    if(this.options.now()-this.last.sentAt>=7*86400000)throw new Error('最初の送信から7日経過しています。最新状態を確認してください。');
    return this.send();
  }
  private async send(){
    const saved=this.last!; const unknown=this.state==='unknown';this.state='sending';let attempted=false;
    try{const csrf=await this.options.csrf();attempted=true;const result=await this.options.post(saved,csrf);this.state='success';return result;}
    catch(error){const known=typeof error==='object'&&error!==null&&'status' in error&&typeof error.status==='number';const status=known?(error as {status:number}).status:0;const unresolved=attempted&&(!known||status>=500)||unknown&&(!attempted||status===401||status===403);this.state=unresolved?'unknown':'failed';throw error;}
  }
}
