// Re-import the collected files only; this never loads another game DB.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict'),cp=require('node:child_process'),crypto=require('node:crypto'),{pathToFileURL}=require('node:url');
const [rootArg,buildArg,sourceFilter='3200014006_0.db']=process.argv.slice(2),root=path.resolve(rootArg),base=path.join(root,'evidence'),build=path.resolve(buildArg);
function geometry(scene){let meshes=0,triangles=0,bones=0;scene.traverse(o=>{if(!o.isMesh)return;meshes++;const g=o.geometry,p=g.getAttribute('position');assert(p&&p.count>0);for(const key of ['position','normal','uv','color'])if(g.getAttribute(key))for(const n of g.getAttribute(key).array)assert(Number.isFinite(n),'Non-finite '+key);if(g.index)for(const i of g.index.array)assert(i>=0&&i<p.count);triangles+=(g.index?.count??p.count)/3;if(o.isSkinnedMesh){bones+=o.skeleton.bones.length;const si=g.getAttribute('skinIndex'),sw=g.getAttribute('skinWeight');assert(si&&sw);for(let i=0;i<si.array.length;i++)if(sw.array[i]>0)assert(si.array[i]>=0&&si.array[i]<o.skeleton.bones.length);}});return{meshes,triangles,bones};}
function triangleHash(scene){const triangles=[];scene.traverse(o=>{if(!o.isMesh)return;const g=o.geometry,p=g.getAttribute('position'),count=g.index?.count??p.count;for(let i=0;i<count;i+=3){const points=[];for(let j=0;j<3;j++){const v=g.index?g.index.getX(i+j):i+j;points.push([p.getX(v),p.getY(v),p.getZ(v)].map(x=>x.toFixed(5)).join(','));}triangles.push([0,1,2].map(j=>[points[j],points[(j+1)%3],points[(j+2)%3]].join(';')).sort()[0]);}});return crypto.createHash('sha256').update(triangles.sort().join('\n')).digest('hex');}
(async()=>{
 const modules=path.resolve(__dirname,'../frontend/node_modules/three'),three=await import(pathToFileURL(path.join(modules,'build/three.module.js')));global.window={URL};three.TextureLoader.prototype.load=function(url){const texture=new three.Texture();texture.name=url;return texture;};
 const {FBXLoader}=await import(pathToFileURL(path.join(modules,'examples/jsm/loaders/FBXLoader.js'))),{OBJLoader}=await import(pathToFileURL(path.join(modules,'examples/jsm/loaders/OBJLoader.js')));
 const latest=new Map();for(const line of fs.readFileSync(path.join(base,'asset-exports.jsonl'),'utf8').split(/\r?\n/).filter(Boolean)){const r=JSON.parse(line);if(/\.db$/i.test(sourceFilter)?path.basename(r.source).toLowerCase()===sourceFilter.toLowerCase():r.source.includes(sourceFilter))latest.set(r.id+'|'+r.format,r);}
 const checks=[],failures=[],skipped=[];const fbx=r=>{const b=fs.readFileSync(r.path);return new FBXLoader().parse(b.buffer.slice(b.byteOffset,b.byteOffset+b.byteLength),path.dirname(r.path)+'/');};
 for(const r of latest.values()){
  if(!r.ok){skipped.push({id:r.id,name:r.name,format:r.format,error:r.error});continue;}
  try{
   if(r.type==='Mesh'&&r.format==='obj'){
    const other=latest.get(r.id+'|fbx');assert(other?.ok,'Missing corresponding FBX');const a=new OBJLoader().parse(fs.readFileSync(r.path,'utf8')),b=fbx(other),ga=geometry(a),gb=geometry(b),meta=JSON.parse(fs.readFileSync(other.path+'.export.json'));
    assert.equal(ga.triangles,meta.triangles);assert.equal(gb.triangles,meta.triangles);assert.equal(triangleHash(a),triangleHash(b));if(meta.bones>0)assert(gb.bones>0);
    checks.push({id:r.id,name:r.name,kind:'OBJ-FBX-independent-reimport',obj:ga,fbx:gb,animations:b.animations.length});
   }else if(r.type==='AnimationClip'&&r.format==='fbx'){
    const scene=fbx(r);assert(scene.animations.length>0);for(const animation of scene.animations){assert(animation.tracks.length>0);for(const track of animation.tracks){for(const x of track.times)assert(Number.isFinite(x));for(const x of track.values)assert(Number.isFinite(x));}}
    const mixer=new three.AnimationMixer(scene);for(const clip of scene.animations){mixer.stopAllAction();mixer.clipAction(clip).play();mixer.setTime(Math.max(0,clip.duration/2));}mixer.stopAllAction();
    checks.push({id:r.id,name:r.name,kind:'FBX-animation-reimport-and-evaluate',takes:scene.animations.length});
   }else if(r.type==='WwiseAudio'&&['original','wem'].includes(r.format)){
    const scratchRoot=path.resolve('.cache'),scratch=fs.mkdtempSync(path.join(scratchRoot,'wem-validation-'));
    try{
     fs.copyFileSync(r.path,path.join(scratch,'input.wem'));
     const p=cp.spawnSync(path.join(build,'worker/media/vgmstream-cli.exe'),['-i','-O','input.wem'],{cwd:scratch,encoding:'utf8',windowsHide:true,timeout:120000,maxBuffer:2*1024*1024});
     assert.equal(p.status,0,p.stderr||p.error?.message);checks.push({id:r.id,name:r.name,kind:'independent-full-WEM-decode'});
    }finally{assert(scratch.startsWith(scratchRoot+path.sep)&&path.basename(scratch).startsWith('wem-validation-'));fs.rmSync(scratch,{recursive:true,force:true});}
   }else if(r.format==='png'||r.format==='mp3'||r.format==='wav'){
    const p=cp.spawnSync(path.join(build,'worker/media/ffmpeg.exe'),['-v','error','-i',r.path,'-f','null','-'],{encoding:'utf8',windowsHide:true,timeout:120000,maxBuffer:2*1024*1024});assert.equal(p.status,0,p.stderr||p.error?.message);checks.push({id:r.id,name:r.name,kind:'independent-media-decode',format:r.format});
   }else if(r.format==='json'||r.format==='curves-json'){
    const parsed=JSON.parse(fs.readFileSync(r.path));assert(parsed!==null);checks.push({id:r.id,name:r.name,kind:'JSON-structure-parse',format:r.format});
   }
  }catch(e){failures.push({id:r.id,name:r.name,format:r.format,error:e.stack});}
 }
 const result={generated:new Date().toISOString(),sourceFilter,passed:checks.length>0&&failures.length===0&&skipped.length===0,fileChecksPassed:checks.length>0&&failures.length===0,allRequestedExportsSucceeded:latest.size>0&&skipped.length===0,checks,failures,unsupportedOrFailedExports:skipped,scope:'File reimport/decoding validation; not proof that every corpus dependency is collected. Texture image loading is disabled in geometry-only FBX checks.'};
 const report='collected-asset-validation-'+crypto.createHash('sha256').update(sourceFilter).digest('hex').slice(0,16)+'.json';fs.writeFileSync(path.join(base,report),JSON.stringify(result,null,2));
 const index=fs.readdirSync(base).filter(f=>/^collected-asset-validation-[a-f0-9]{16}\.json$/.test(f)).map(f=>{const r=JSON.parse(fs.readFileSync(path.join(base,f)));return{sourceFilter:r.sourceFilter,report:f,generated:r.generated,fileChecksPassed:r.fileChecksPassed,allRequestedExportsSucceeded:r.allRequestedExportsSucceeded,checks:r.checks.length,failures:r.failures.length,unsupportedOrFailedExports:r.unsupportedOrFailedExports.length};});
 fs.writeFileSync(path.join(base,'collected-asset-validation.json'),JSON.stringify({generated:result.generated,corpusComplete:false,reports:index},null,2));console.log(JSON.stringify({report,passed:result.passed,fileChecksPassed:result.fileChecksPassed,checks:checks.length,kinds:checks.reduce((s,c)=>(s[c.kind]=(s[c.kind]??0)+1,s),{}),failureCount:failures.length,unsupportedOrFailedCount:skipped.length,failureSample:failures.slice(0,5),unsupportedSample:skipped.slice(0,5),consoleDetailsOmitted:Math.max(0,failures.length-5)+Math.max(0,skipped.length-5),allDetailsPreservedInReport:true},null,2));if(failures.length)process.exitCode=1;
})().catch(e=>{console.error(e);process.exitCode=1});
