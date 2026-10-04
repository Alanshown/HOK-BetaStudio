import type {Db} from './types';
import './unknown-db.css';

export function UnknownDbList({files,title,label,selected,busy,onSelect}:{files:Db[];title:string;label:string;selected:string|null;busy:boolean;onSelect:(id:string)=>void}){
 const groups=new Map<string,Db[]>();
 for(const file of files){const group=groups.get(file.skinId)||[];group.push(file);groups.set(file.skinId,group)}
 const packages=[...groups.entries()].sort((a,b)=>a[1][0].path.localeCompare(b[1][0].path));
 if(!packages.length)return null;
 return <section className="unknown-db-panel" aria-label={title}><header><h2>{title}</h2><span className="count">{files.length}</span></header><div className="unknown-db-list">{packages.map(([id,records])=>{
  const sorted=[...records].sort((a,b)=>Number(a.shard!==null)-Number(b.shard!==null)||a.name.localeCompare(b.name));
  return <button className={'unknown-db-row '+(selected===id?'selected':'')} key={id} disabled={busy} onClick={()=>onSelect(id)}><span className="unknown-db-badge">{label}</span><span className="unknown-db-details"><strong>{sorted.map(d=>d.name).join(' · ')}</strong><span className="mono" title={sorted[0].path}>{sorted[0].path.replace(/[\\/][^\\/]+$/,'')}</span></span></button>
 })}</div></section>
}
