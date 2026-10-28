export type NamedRef = { id: string; name: string };
export type ImageRef = { linkId:string; assetId:string; sortOrder:number; isPrimary:boolean; thumbnailUrl:string; displayUrl:string; originalUrl:string|null };
export type ProductSummary = { id: string; name: string; category: NamedRef; works: NamedRef[]; totalQuantity: number; conditions: (string|null)[]; storageLocations: string[]; primaryImage: ImageRef|null; createdAt: string; updatedAt: string; revision: string; deletedAt?:string|null };
export type SearchPage = { items: ProductSummary[]; page: number; pageSize: number; totalCount: number; totalPages: number; dataRevision: string };
export type CollectionSummary = { productCount: number; totalQuantity: number; recentProducts: ProductSummary[] };
export type Holding = { id: string; quantity?: number; images?:ImageRef[]; condition: string|null; storageLocation: string|null; purchasePlace: string|null; memo: string|null };
export type ProductDetail = ProductSummary & { images:ImageRef[]; version: string|null; memo: string|null; manufacturer: NamedRef|null; seller: NamedRef|null; characters: NamedRef[]; tags: NamedRef[]; identifiers: { id:string; scheme:string; rawValue:string }[]; links: {id:string;url:string}[]; inventory:{ lots:Holding[]; items:Holding[] } };
export type Session = { user: { id:string; loginId: string; isAdministrator:boolean }; expiresAt: string; persistent: boolean };
