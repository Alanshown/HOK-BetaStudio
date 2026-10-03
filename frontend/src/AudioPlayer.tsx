import {useEffect,useRef,useState} from 'react';
import type {Text} from './i18n';
const time=(n:number)=>Number.isFinite(n)?`${Math.floor(n/60)}:${String(Math.floor(n%60)).padStart(2,'0')}`:'0:00';
export function AudioPlayer({url,art,t}:{url:string;art:string;t:Text}){
 const audio=useRef<HTMLAudioElement>(null);
 const [playing,setPlaying]=useState(false),[duration,setDuration]=useState(0),[position,setPosition]=useState(0),[volume,setVolume]=useState(1),[error,setError]=useState('');
 useEffect(()=>{const element=audio.current;return()=>{if(element){element.pause();element.removeAttribute('src');element.load()}}},[url]);
 async function toggle(){const element=audio.current;if(!element)return;try{if(element.paused)await element.play();else element.pause()}catch{setError(t.audioError)}}
 return <div className={'audio-player '+(playing?'playing':'')}>
  <audio ref={audio} src={url} preload="metadata" onLoadedMetadata={e=>setDuration(e.currentTarget.duration)} onTimeUpdate={e=>setPosition(e.currentTarget.currentTime)} onPlay={()=>setPlaying(true)} onPause={()=>setPlaying(false)} onEnded={()=>setPlaying(false)} onError={()=>setError(t.audioError)}/>
  <div className="audio-emblem"><img src={art+'icons/ui/audio-lines.svg'} alt=""/></div>
  {error?<p role="alert">{error}</p>:<>
   <button className="audio-play" onClick={()=>void toggle()} aria-label={playing?t.pauseAudio:t.playAudio}><img src={art+'icons/ui/'+(playing?'pause':'play')+'.svg'} alt=""/></button>
   <div className="audio-timeline"><span>{time(position)}</span><input type="range" aria-label={t.seekAudio} min="0" max={duration||0} step="0.01" value={Math.min(position,duration||0)} disabled={!duration} onChange={e=>{const n=Number(e.target.value);if(audio.current)audio.current.currentTime=n;setPosition(n)}}/><span>{time(duration)}</span></div>
   <label className="audio-volume">{t.volumeAudio}<input type="range" aria-label={t.volumeAudio} min="0" max="1" step="0.01" value={volume} onChange={e=>{const n=Number(e.target.value);setVolume(n);if(audio.current)audio.current.volume=n}}/></label>
  </>}
 </div>
}
