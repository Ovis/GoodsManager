/** Clipboard APIがない・拒否された場合も、端末の手動コピーへ進める。 */
export async function copyPhotoText(value:string,clipboard:Pick<Clipboard,'writeText'>|undefined,textarea:Pick<HTMLTextAreaElement,'focus'|'select'>|null){
  try{
    if(!clipboard)throw Error();
    await clipboard.writeText(value);
    return '全文をコピーしました。';
  }catch{
    textarea?.focus();textarea?.select();
    return '全文を選択しました。端末のコピー操作を使ってください。';
  }
}
