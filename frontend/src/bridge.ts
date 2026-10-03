import type {Boot} from './types';
interface Host {postMessage(data:unknown):void;postMessageWithAdditionalObjects?(data:unknown,objects:File[]):void;addEventListener(type:string,listener:(event:{data:any})=>void):void;}
declare global {interface Window {chrome?:{webview?:Host};}}
const pending=new Map<string,{resolve:(v:any)=>void;reject:(e:Error)=>void;timer:number}>();
export const desktop=Boolean(window.chrome?.webview);
window.chrome?.webview?.addEventListener('message',e=>{const p=pending.get(e.data.id);if(!p)return;clearTimeout(p.timer);pending.delete(e.data.id);e.data.ok?p.resolve(e.data.data):p.reject(new Error(e.data.error));});
export async function rpc<T=any>(method:string,payload:unknown={},files?:File[]):Promise<T>{
 if(!desktop){if(method==='boot'){const [heroes,skins]=await Promise.all(['heroes','skins'].map(x=>fetch('/assets/catalog/'+x+'.json').then(r=>r.json())));return {version:'1.2',heroes:heroes.heroes,skins:skins.skins,artOrigin:'/assets/',initialPaths:[]} as T;}throw new Error('DESKTOP_REQUIRED');}
 return new Promise((resolve,reject)=>{const id=crypto.randomUUID();const timer=window.setTimeout(()=>{pending.delete(id);reject(new Error('Request timed out'));},420000);pending.set(id,{resolve,reject,timer});try{const host=window.chrome!.webview!;if(files){if(!host.postMessageWithAdditionalObjects)throw new Error('WebView2 file drop is unavailable; update the WebView2 Runtime.');host.postMessageWithAdditionalObjects({id,method,payload},files)}else host.postMessage({id,method,payload})}catch(e){clearTimeout(timer);pending.delete(id);reject(e instanceof Error?e:new Error(String(e)))}});
}
export const boot=()=>rpc<Boot>('boot');
export function placeholder(id:string){let hash=2166136261;for(const byte of new TextEncoder().encode(id))hash=Math.imul(hash^byte,16777619)>>>0;return ['skin-jade-lotus','skin-tidal-ring','skin-cloud-feather','skin-faceted-seal'][hash%4];}
