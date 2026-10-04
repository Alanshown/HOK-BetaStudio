import {useEffect,useState} from 'react';
import {desktop,rpc} from './bridge';
import type {Boot,Lang} from './types';
import './resources.css';

export interface ResourceUpdate {revision:string;added:number;changed:number}
const labels={
 zh:{sync:'资源同步',detail:(a:number,c:number)=>`新增 ${a} 项，变更 ${c} 项。同步将清空导入缓存与待重建的替换记录，原始 DB 和导出文件不变。`},
 en:{sync:'Resource sync',detail:(a:number,c:number)=>`${a} added, ${c} changed. Sync clears imported data and staged replacements. Original DBs and exports stay untouched.`},
 vi:{sync:'Đồng bộ tài nguyên',detail:(a:number,c:number)=>`${a} mục mới, ${c} mục thay đổi. Đồng bộ xóa bộ nhớ đệm đã nhập và thay thế chưa đóng gói. DB gốc và tệp đã xuất được giữ nguyên.`},
};

// Checking deliberately has no rendered state, notification, spinner or progress bar.
export function useResourceUpdate(ready:boolean){
 const [update,setUpdate]=useState<ResourceUpdate|null>(null);
 useEffect(()=>{if(!ready||!desktop)return;let alive=true;void rpc<ResourceUpdate|null>('catalogCheck').then(value=>{if(alive)setUpdate(value)}).catch(()=>{});return()=>{alive=false}},[ready]);
 return {update,clearUpdate:()=>setUpdate(null)};
}

export function ResourceSync({update,lang,onApply}:{update:ResourceUpdate|null;lang:Lang;onApply:(revision:string)=>Promise<void>}){
 const [applying,setApplying]=useState(false);
 if(!update)return null;
 const text=labels[lang]||labels.zh;
 return <button className={'resource-sync '+(applying?'applying':'')} disabled={applying} aria-label={text.sync} aria-busy={applying} title={text.detail(update.added,update.changed)} onClick={()=>{if(applying)return;setApplying(true);void onApply(update.revision).finally(()=>setApplying(false))}}>
  <svg viewBox="0 0 24 24" width="21" height="21" fill="none" aria-hidden="true"><path d="M19.3 9.2A7.6 7.6 0 0 0 6.2 6.6L3.5 9.5m0-5v5h5M4.7 14.8a7.6 7.6 0 0 0 13.1 2.6l2.7-2.9m0 5v-5h-5" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round"/><circle cx="12" cy="12" r="2" fill="currentColor" opacity=".25"/></svg>
  <span>{text.sync}</span><i className="resource-dot"/>
 </button>;
}

export function portraitUrl(value:string|undefined,art:string,revision:string,fallback:string){
 if(value?.startsWith('https://')){
  try{const url=new URL(value);if(['game-1255653016.file.myqcloud.com','image.smoba.qq.com'].includes(url.hostname)&&!url.username&&!url.password&&(!url.port||url.port==='443')){url.searchParams.set('hbs_rev',revision);return url.href}}catch{}
 }
 return art+fallback;
}

// A failed remote portrait falls back once, without an image-error retry loop.
export function Portrait({src,fallback,alt,className}:{src:string;fallback:string;alt:string;className?:string}){
 const [failed,setFailed]=useState(false);
 useEffect(()=>setFailed(false),[src]);
 return <img className={className} src={failed?fallback:src} alt={alt} decoding="async" loading="lazy" referrerPolicy="no-referrer" onError={()=>setFailed(true)}/>;
}

export type AppliedCatalog=Pick<Boot,'heroes'|'skins'|'revision'>;
