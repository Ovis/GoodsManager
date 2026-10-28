export const identifierOptions = [
  ['ean13', '商品バーコード（13桁）'],
  ['ean8', '商品バーコード（8桁）'],
  ['upca', '商品バーコード（12桁）'],
  ['isbn10', '本の番号（10桁・ISBN）'],
  ['isbn13', '本の番号（13桁・ISBN）'],
] as const;

export const identifierHelp = '商品は包装や値札のバーコードの下にある数字を、本は「ISBN」と書かれた番号を入力します。手入力では番号の桁数に合わせて種類を選んでください。カメラで読み取ることもできます。';
export const identifierOptionalHelp = 'コードがない場合は空欄で構いません。';
