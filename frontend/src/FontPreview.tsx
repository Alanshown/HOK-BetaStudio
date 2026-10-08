import {useEffect,useState} from 'react';
import type {Text} from './i18n';

export function FontPreview({url,t}:{url:string;t:Text}){
 const [family,setFamily]=useState(''),[error,setError]=useState(''),[sample,setSample]=useState('HOK BetaStudio\nABCDEFGHIJKLMNOPQRSTUVWXYZ\nabcdefghijklmnopqrstuvwxyz\n0123456789');
 useEffect(()=>{
  const controller=new AbortController();let face:FontFace|undefined,active=true;setFamily('');setError('');
  fetch(url,{signal:controller.signal}).then(r=>{if(!r.ok)throw Error(String(r.status));return r.arrayBuffer()}).then(async bytes=>{
   if(!active)return;face=new FontFace('hok-preview-'+crypto.randomUUID(),bytes);await face.load();if(!active)return;document.fonts.add(face);setFamily(face.family);
  }).catch(e=>{if(active)setError(String(e))});
  return()=>{active=false;controller.abort();if(face)document.fonts.delete(face);face=undefined};
 },[url]);
 if(error)return <div className="preview-failure">{error}</div>;
 if(!family)return <div className="stage-state">{t.loading}</div>;
 return <div className="font-preview" data-font-ready="true"><textarea aria-label={t.fontSample} value={sample} onChange={e=>setSample(e.target.value)} style={{fontFamily:`"${family}"`,fontSize:36,width:'100%',minHeight:320,padding:28,lineHeight:1.6,border:0,background:'transparent',color:'inherit',resize:'vertical'}}/></div>;
}
