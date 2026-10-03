import {useEffect,useRef,useState} from 'react';
import {rpc} from './bridge';
import type {Asset} from './types';
import type {Text} from './i18n';
import {AudioPlayer} from './AudioPlayer';
import {CrystalButton} from './CrystalButton';
import './bank.css';
export function BankPreview({bank,art,t,exportBusy,onExport}:{bank:Asset;art:string;t:Text;exportBusy:boolean;onExport:(bank:Asset,format:string)=>Promise<void>}){
 const [items,setItems]=useState<Asset[]>([]),[loaded,setLoaded]=useState(false),[selected,setSelected]=useState(-1),[expanded,setExpanded]=useState(true),[url,setUrl]=useState(''),[error,setError]=useState(''),[mediaError,setMediaError]=useState(''),[exportError,setExportError]=useState('');
 const ticket=useRef(0),alive=useRef(true),buttons=useRef<(HTMLButtonElement|null)[]>([]);
 useEffect(()=>{alive.current=true;const version=++ticket.current;
  void rpc<{items:Asset[]}>('bank',{assetId:bank.id}).then(result=>{if(!alive.current||ticket.current!==version)return;setItems(result.items);setLoaded(true);if(result.items.length)select(0,result.items)}).catch(e=>{if(alive.current&&ticket.current===version){setError(String(e.message||e));setLoaded(true)}});
  return()=>{alive.current=false;ticket.current++};
 },[bank.id]);
 function select(index:number,list=items){const item=list[index];if(!item)return;const version=++ticket.current;setSelected(index);setUrl('');setMediaError('');
  void rpc<{url:string;kind:string}>('preview',{assetId:item.id}).then(data=>{if(alive.current&&version===ticket.current)setUrl(data.url)}).catch(e=>{if(alive.current&&version===ticket.current)setMediaError(String(e.message||e))});
 }
 function navigate(index:number){setExpanded(true);select(index);buttons.current[index]?.focus();buttons.current[index]?.scrollIntoView({block:'nearest'});}
 async function exportFile(format:string){setExportError('');try{await onExport(bank,format)}catch(e){if(alive.current)setExportError(e instanceof Error?e.message:String(e))}}
 const current=items[selected];
 return <div className="bank-preview">
  <div className="bank-split">
   <aside className="bank-tree" role="tree" aria-label={t.bankContents}>
    <div role="treeitem" aria-expanded={expanded} className="bank-root">
     <button className="bank-root-button" onClick={()=>setExpanded(!expanded)} aria-label={bank.name} aria-expanded={expanded}><img src={art+'icons/ui/'+(expanded?'chevron-down':'chevron-right')+'.svg'} alt=""/><img src={art+'icons/ui/folder-open.svg'} alt=""/><span>{bank.name}</span>{loaded&&!error&&<small>{items.length}</small>}</button>
     {expanded&&<div role="group" className="bank-children">{items.map((item,index)=><button key={item.id} ref={e=>{buttons.current[index]=e}} role="treeitem" aria-selected={index===selected} tabIndex={index===selected?0:-1} title={item.name} className={'bank-leaf '+(index===selected?'active':'')} onClick={()=>select(index)} onKeyDown={e=>{if(e.key==='ArrowDown'||e.key==='ArrowUp'||e.key==='Home'||e.key==='End'){e.preventDefault();navigate(e.key==='Home'?0:e.key==='End'?items.length-1:Math.max(0,Math.min(items.length-1,index+(e.key==='ArrowDown'?1:-1))))}}}><img src={art+'icons/ui/audio-lines.svg'} alt=""/><span>{item.name}</span></button>)}</div>}
    </div>
   </aside>
   <section className="bank-player" aria-label={t.audio}>
    {error?<p role="alert" className="bank-state">{error}</p>:!loaded?<p className="bank-state">{t.loading}</p>:!current?<p className="bank-state">{t.bankEmpty}</p>:<>
     <div className="bank-track"><strong>{current.name}</strong><span className="mono">{current.pathId}</span></div>
     <div className="bank-audio">{mediaError?<p role="alert" className="bank-state">{mediaError}</p>:url?<AudioPlayer key={current.id} url={url} art={art} t={t}/>:<p className="bank-state">{t.loading}</p>}</div>
     <div className="preview-navigation"><CrystalButton direction="left" art={art} label={t.previousAsset} disabled={selected<=0} onClick={()=>navigate(selected-1)}/><span className="mono">{selected+1} / {items.length}</span><CrystalButton direction="right" art={art} label={t.nextAsset} disabled={selected>=items.length-1} onClick={()=>navigate(selected+1)}/></div>
    </>}
   </section>
  </div>
  {exportError&&<p role="alert" className="bank-export-error">{exportError}</p>}
  <div className="bank-actions"><button className="button" disabled={exportBusy} onClick={()=>void exportFile('original')}>{t.originalBank}</button><button className="button" disabled={exportBusy||!items.length||!!error} onClick={()=>void exportFile('zip-wem')}>{t.zipWem}</button><button className="button primary" disabled={exportBusy||!items.length||!!error||!bank.formats.includes('zip-mp3')} onClick={()=>void exportFile('zip-mp3')}>{t.zipMp3}</button>{exportBusy&&<span className="quiet" role="status">{t.exportBackground}</span>}</div>
 </div>
}
