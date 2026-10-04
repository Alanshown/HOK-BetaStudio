export type Lang='zh'|'en'|'vi';
export interface Hero{id:string;name:string;portrait:string;}
export interface Skin{skinId:string;heroId:string;name:string;portrait:string;artAvailable:boolean;}
export interface Db{id:string;path:string;name:string;root:string;heroId:string;skinId:string;stem:string;shard:string|null;bytes:number;fingerprint:string;signature:string;}
export interface Asset{id:string;name:string;type:string;pathId:string;source:string;bytes:string;formats:string[];preview:string;}
export interface Boot{version:string;revision:string;heroes:Hero[];skins:Skin[];artOrigin:string;initialPaths:string[];}
export interface LoadResult{generation:number;result:{count:number;serializedFiles:number;errors:string[];types:Record<string,number>;};}
export interface Page{total:number;page:number;items:Asset[];}
