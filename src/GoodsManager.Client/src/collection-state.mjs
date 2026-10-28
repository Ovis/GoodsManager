export const multiFields = ['workIds','seriesIds','characterIds','categoryIds','tagIds','manufacturerIds','sellerIds','conditions'];
export const filterFields = ['q','barcode',...multiFields,'storageLocation','purchasePlace','hasImages','quantityMin','quantityMax','inventory'];
export function apiSearch(search) { const p = new URLSearchParams(search); p.delete('view'); p.delete('collectionId'); return p.toString(); }
export function updateSearch(search, changes) {
  const p = new URLSearchParams(search);
  for (const [key,value] of Object.entries(changes)) {
    p.delete(key);
    for (const entry of Array.isArray(value) ? value : [value]) if (entry !== '' && entry !== null && entry !== undefined) p.append(key,String(entry));
  }
  if (Object.keys(changes).some(k => k !== 'view' && k !== 'page')) p.delete('page');
  return p.toString();
}
export function isFiltered(search) { const p = new URLSearchParams(search); return filterFields.some(k => k === 'inventory' ? p.has(k) && p.get(k) !== 'positive' : p.getAll(k).some(Boolean)); }
export function pageNumbers(page,total) { return Array.from(new Set([1,total,...Array.from({length:5},(_,i)=>page+i-2)].filter(n=>n>=1&&n<=total))).sort((a,b)=>a-b); }
export function safeReturn(path) { return /^\/(?:collections|masters|trash|backups|account)(?:\?|$)/.test(path) || /^\/trash\/products\/[0-9a-f-]{36}(?:\?|$)/i.test(path) || /^\/register(?:\?collectionId=[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})?$/i.test(path) || /^\/collection(?:\?|$)/.test(path) || /^\/products\/[0-9a-f-]{36}(?:\/items\/[0-9a-f-]{36})?(?:\?|$)/i.test(path) ? path : '/collection'; }
export function canonicalPage(search,page) { const p = new URLSearchParams(search); if (String(page) !== (p.get('page') || '1')) { if(page===1)p.delete('page');else p.set('page',String(page)); } return p.toString(); }
