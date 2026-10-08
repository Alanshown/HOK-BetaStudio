import {useEffect,useMemo,useRef,useState} from 'react';
import type {Text} from './i18n';
import './animation-preview.css';
type Key={time:number;value:number[]};type Track={path:string;property:string;keys:Key[]};type Curves={duration:number;tracks:Track[];objectReferenceCurves:number};
export function CurvePreview({url,t}:{url:string;t:Text}){
 const [data,setData]=useState<Curves|null>(null),[error,setError]=useState(''),[selected,setSelected]=useState(0),[time,setTime]=useState(0),[playing,setPlaying]=useState(false);const clock=useRef(0);
 useEffect(()=>{const c=new AbortController();setData(null);setError('');setSelected(0);setTime(0);setPlaying(false);clock.current=0;fetch(url,{signal:c.signal}).then(r=>{if(!r.ok)throw Error(String(r.status));return r.json()}).then(setData).catch(e=>{if(!c.signal.aborted)setError(String(e))});return()=>c.abort()},[url]);
 useEffect(()=>{if(!playing||!data?.duration)return;let frame=0,previous=performance.now();const tick=(now:number)=>{clock.current=(clock.current+Math.min(.1,(now-previous)/1000))%data.duration;previous=now;setTime(clock.current);frame=requestAnimationFrame(tick)};frame=requestAnimationFrame(tick);return()=>cancelAnimationFrame(frame)},[playing,data]);
 const track=data?.tracks[selected],duration=data?.duration||1;
 // Geometry is independent of the playhead. Build it only when the selected
 // track changes, retaining bucket extrema for very long clips instead of
 // allocating/serializing every key again on every animation frame.
 const plot=useMemo(()=>{
  const keys=track?[...track.keys].sort((a,b)=>a.time-b.time):[];
  let min=Infinity,max=-Infinity;for(const key of keys)for(const value of key.value){min=Math.min(min,value);max=Math.max(max,value);}
  if(!Number.isFinite(min)){min=0;max=1;}const height=max-min||1;
  const lines=(keys[0]?.value||[]).map((_,axis)=>{
   const sample:Key[]=[];const step=Math.max(1,Math.ceil(keys.length/1800));
   for(let start=0;start<keys.length;start+=step){let low=start,high=start;const end=Math.min(keys.length,start+step);for(let i=start+1;i<end;i++){if(keys[i].value[axis]<keys[low].value[axis])low=i;if(keys[i].value[axis]>keys[high].value[axis])high=i;}for(const i of [...new Set([start,low,high,end-1])].sort((a,b)=>a-b))sample.push(keys[i]);}
   return sample.map(k=>`${40+k.time/duration*880},${270-(k.value[axis]-min)/height*230}`).join(' ');
  });return{keys,lines};
 },[track,duration]);
 if(!data)return <div className="stage-state">{error||t.loading}</div>;
 const x=(v:number)=>40+v/duration*880;
 let lo=0,hi=plot.keys.length;while(lo<hi){const mid=(lo+hi)>>>1;if(plot.keys[mid].time<=time)lo=mid+1;else hi=mid;}const key=plot.keys[Math.max(0,lo-1)];
 return <div className="curve-preview" data-curve-preview="true"><p className="quiet">{t.animationCurveOnly}</p><label>{t.animationTrack}<select value={selected} onChange={e=>setSelected(Number(e.target.value))}>{data.tracks.map((c,i)=><option key={i} value={i}>{c.path||'/'} · {c.property}</option>)}</select></label>{!track?<p>{t.animationNoTracks}</p>:<><svg viewBox="0 0 960 310" role="img" aria-label={t.animationKeyframes}><path d="M40 20V270H930" fill="none" stroke="#adcac5"/>{plot.lines.map((points,axis)=><polyline key={axis} points={points} fill="none" stroke={['#17877a','#417fd3','#ae68ad','#b07c36'][axis]} strokeWidth="1.4"/>)}<line x1={x(time)} x2={x(time)} y1="20" y2="275" stroke="#244b47" strokeDasharray="4 4"/><text x="42" y="300" fill="#52706b">0 s</text><text x="855" y="300" fill="#52706b">{data.duration.toFixed(2)} s</text></svg><div className="curve-values mono">{key&&<>{t.animationKeyframes}: {key.time.toFixed(3)} s · {key.value.map(n=>n.toPrecision(6)).join(' / ')}</>}</div></>}<div className="curve-controls"><button onClick={()=>setPlaying(p=>!p)} disabled={!data.duration}>{playing?t.pauseAudio:t.playAudio}</button><input type="range" aria-label={t.seekAudio} min="0" max={duration} step=".001" value={time} onChange={e=>{clock.current=Number(e.target.value);setTime(clock.current)}}/><span className="mono">{time.toFixed(2)} s</span></div></div>;
}
