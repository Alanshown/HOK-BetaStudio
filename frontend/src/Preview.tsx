import {useEffect,useRef,useState} from 'react';
import * as THREE from 'three';
import {OBJLoader} from 'three/addons/loaders/OBJLoader.js';
import {FBXLoader} from 'three/addons/loaders/FBXLoader.js';
import {OrbitControls} from 'three/addons/controls/OrbitControls.js';
import type {Text} from './i18n';
import './animation-preview.css';
export function ModelView({url,t,reduced,animated=false,warnings=[]}:{url:string;t:Text;reduced:boolean;animated?:boolean;warnings?:string[]}){
 const host=useRef<HTMLDivElement>(null),control=useRef<OrbitControls|null>(null),group=useRef<THREE.Object3D|null>(null);
 const [auto,setAuto]=useState(!reduced),[wire,setWire]=useState(false),[err,setErr]=useState(''),[ready,setReady]=useState(false);
 const mixer=useRef<THREE.AnimationMixer|null>(null),playingRef=useRef(true);
 const [playing,setPlaying]=useState(true),[duration,setDuration]=useState(0),[time,setTime]=useState(0);
 useEffect(()=>{playingRef.current=playing},[playing]);
 const reset=useRef(()=>{});useEffect(()=>{if(control.current)control.current.autoRotate=auto},[auto]);
 useEffect(()=>{let disposed=false,frame=0;const box=host.current!;const renderer=new THREE.WebGLRenderer({antialias:true,alpha:true});renderer.setPixelRatio(Math.min(devicePixelRatio,2));renderer.setClearColor(0xf1f6f5,0);box.appendChild(renderer.domElement);
 const scene=new THREE.Scene(),camera=new THREE.PerspectiveCamera(38,1,.001,100000);
 scene.add(new THREE.HemisphereLight(0xffffff,0x879e94,2.5));const key=new THREE.DirectionalLight(0xffffff,3);key.position.set(4,8,6);scene.add(key);
 const controls=new OrbitControls(camera,renderer.domElement);control.current=controls;controls.enableDamping=true;controls.autoRotate=!reduced;controls.autoRotateSpeed=1.1;
 const stop=()=>{controls.autoRotate=false;setAuto(false)};renderer.domElement.addEventListener('pointerdown',stop);renderer.domElement.addEventListener('wheel',stop,{passive:true});
 const resize=()=>{if(!box.clientWidth||!box.clientHeight)return;renderer.setSize(box.clientWidth,box.clientHeight);camera.aspect=box.clientWidth/box.clientHeight;camera.updateProjectionMatrix()};const observer=new ResizeObserver(resize);observer.observe(box);
 const release=(o:THREE.Object3D)=>{const textures=new Set<THREE.Texture>();o.traverse(child=>{if(child instanceof THREE.Mesh){child.geometry.dispose();const mats=Array.isArray(child.material)?child.material:[child.material];mats.forEach(m=>{Object.values(m).forEach(v=>{if(v instanceof THREE.Texture)textures.add(v)});m.dispose()});if(child instanceof THREE.SkinnedMesh)child.skeleton.dispose()}});textures.forEach(t=>t.dispose())};
 const loaded=(obj:THREE.Group)=>{if(disposed){release(obj);return;}if(!animated)obj.traverse(c=>{if(c instanceof THREE.Mesh){const old=Array.isArray(c.material)?c.material:[c.material];old.forEach(m=>m.dispose());c.material=new THREE.MeshStandardMaterial({color:0xbbd4cf,roughness:.68,metalness:.08,side:THREE.DoubleSide});}});
  const bounds=new THREE.Box3().setFromObject(obj),center=bounds.getCenter(new THREE.Vector3()),size=bounds.getSize(new THREE.Vector3()),radius=Math.max(size.x,size.y,size.z,.01);
  // Center through a parent so animation tracks cannot overwrite the offset.
  const pivot=new THREE.Group();pivot.position.sub(center);pivot.add(obj);scene.add(pivot);group.current=pivot;
  if(animated){const clip=obj.animations[0];if(!clip||!clip.tracks.length){setErr(t.animationNoTracks)}else{mixer.current=new THREE.AnimationMixer(obj);mixer.current.clipAction(clip).setLoop(THREE.LoopRepeat,Infinity).play();setDuration(clip.duration)}}
  reset.current=()=>{camera.position.set(radius*.75,radius*.45,radius*1.8);camera.near=radius/1000;camera.far=radius*100;camera.updateProjectionMatrix();controls.target.set(0,0,0);controls.update()};reset.current();setReady(true);
 };
 const failed=(e:unknown)=>{if(!disposed)setErr(e instanceof Error?e.message:String(e))};
 const manager=new THREE.LoadingManager();manager.onError=()=>{if(!disposed)setErr(t.animationTextures)};
 const request=new AbortController();
 void fetch(url,{signal:request.signal}).then(async response=>{if(!response.ok)throw Error(String(response.status));if(animated){const data=await response.arrayBuffer();if(!disposed)loaded(new FBXLoader(manager).parse(data,new URL('.',url).href))}else{const data=await response.text();if(!disposed)loaded(new OBJLoader(manager).parse(data))}}).catch(failed);
 let last=performance.now(),lastLabel=0;const animate=(now:number)=>{if(disposed)return;frame=requestAnimationFrame(animate);const dt=Math.min((now-last)/1000,.1);last=now;if(document.hidden)return;controls.update(dt);if(mixer.current&&playingRef.current)mixer.current.update(dt);if(now-lastLabel>100&&mixer.current){setTime(mixer.current.time);lastLabel=now}renderer.render(scene,camera)};frame=requestAnimationFrame(animate);
 return()=>{disposed=true;request.abort();cancelAnimationFrame(frame);observer.disconnect();renderer.domElement.removeEventListener('pointerdown',stop);renderer.domElement.removeEventListener('wheel',stop);controls.dispose();if(mixer.current){mixer.current.stopAllAction();mixer.current.uncacheRoot(mixer.current.getRoot());mixer.current=null}if(group.current)release(group.current);group.current=null;control.current=null;reset.current=()=>{};renderer.dispose();renderer.forceContextLoss();renderer.domElement.remove()};
 },[url]);
 useEffect(()=>{group.current?.traverse(c=>{if(c instanceof THREE.Mesh)(Array.isArray(c.material)?c.material:[c.material]).forEach(m=>(m as THREE.MeshStandardMaterial).wireframe=wire)})},[wire,ready]);
 return <div className="model-stage" data-animation-ready={animated&&ready&&duration>0?'true':undefined}><div className="canvas-host" ref={host}/>{!ready&&<div className="stage-state">{err||t.loading}</div>}{(warnings.length>0||(ready&&err))&&<div className="animation-notice" role="status"><strong>{t.animationIncomplete}</strong>{warnings.map(w=><p key={w}>{w==='particles'?t.animationParticles:w==='materials'?t.animationTextures:w==='variants'?t.animationVariants:t.animationProperties}</p>)}{ready&&err&&<p>{err}</p>}</div>}<div className="stage-tools">{animated&&duration>0&&<><button aria-label={playing?t.pauseAudio:t.playAudio} onClick={()=>{const next=!playingRef.current;playingRef.current=next;setPlaying(next);if(mixer.current)setTime(mixer.current.time)}}>{playing?t.pauseAudio:t.playAudio}</button><input aria-label={t.seekAudio} type="range" min={0} max={duration} step={.01} value={time%duration} onChange={e=>{const n=Number(e.target.value);mixer.current?.setTime(n);setTime(n)}}/><span className="mono">{(time%duration).toFixed(1)} / {duration.toFixed(1)}s</span></>}<button aria-pressed={auto} onClick={()=>setAuto(!auto)}>{t.autoRotate}</button><button aria-pressed={wire} onClick={()=>setWire(!wire)}>{t.wireframe}</button><button onClick={()=>{setAuto(false);reset.current()}}>{t.reset}</button></div></div>
}
