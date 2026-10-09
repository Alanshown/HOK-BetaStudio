// One scoped DB load. The archive directory is not a claim of complete
// Android/DEX semantics; native members must all remain independently usable.
const fs=require('node:fs'),path=require('node:path'),cp=require('node:child_process'),rl=require('node:readline'),crypto=require('node:crypto'),assert=require('node:assert/strict');
const [buildArg,sourceArg,baseArg]=process.argv.slice(2),build=path.resolve(buildArg),source=path.resolve(sourceArg),base=path.resolve(baseArg);
const entry='17997877637375806120',output=path.join(base,'probes','zip-worker-'+Date.now()),cache=path.join(output,'cache');fs.mkdirSync(cache,{recursive:true});
const worker=cp.spawn(path.join(build,'worker/Hok.Worker.exe'),[cache],{cwd:path.join(build,'worker'),windowsHide:true}),log=fs.createWriteStream(path.join(output,'worker.log'));worker.stderr.pipe(log);
const pending=new Map();let serial=0;const exited=new Promise(resolve=>worker.once('exit',resolve));
rl.createInterface({input:worker.stdout}).on('line',line=>{const r=JSON.parse(line),p=pending.get(r.id);if(p){clearTimeout(p.timer);pending.delete(r.id);r.ok?p.resolve(r.data):p.reject(Error(r.error.message));}});
worker.once('exit',code=>{for(const p of pending.values()){clearTimeout(p.timer);p.reject(Error('Worker exited '+code));}pending.clear();});
const rpc=(method,payload)=>new Promise((resolve,reject)=>{const id=String(++serial),timer=setTimeout(()=>{worker.kill();reject(Error('Timeout '+method));},300000);pending.set(id,{resolve,reject,timer});worker.stdin.write(JSON.stringify({id,method,payload})+'\n');});
async function hash(file){const h=crypto.createHash('sha256');for await(const b of fs.createReadStream(file))h.update(b);return h.digest('hex');}
(async()=>{try{
 const sourceSha256=await hash(source),load=await rpc('load',{paths:[source],qtsEntrySelection:{[source]:[entry]}});
 assert.equal(load.errorCount,0,JSON.stringify(load.errors));assert.equal(load.types.AndroidPackage,1);assert.equal(load.count,3140);assert.equal(load.materializedResourceBytes,0);
 const rows=[];for(let page=0;;page++){const r=await rpc('list',{page});rows.push(...r.items);if((page+1)*200>=r.total)break;}
 assert.equal(new Set(rows.map(r=>r.id)).size,3140);assert.equal(rows.filter(r=>r.type==='ImageFile').length,1128);
 const exported=[];for(let i=0;i<rows.length;i+=20){const r=await rpc('export',{assetIds:rows.slice(i,i+20).map(r=>r.id),format:'original',output:path.join(output,'native')});assert.equal(r.failed,0,JSON.stringify(r.results));for(const item of r.results)exported.push({...rows.find(x=>x.id===item.id),...item});if(i%500===0)console.log(JSON.stringify({nativeExported:exported.length,total:rows.length}));}
 const previews=[],pngExports=[];
 for(const extension of ['png','jpg','webp','gif']){
  const row=rows.find(r=>r.type==='ImageFile'&&r.name.toLowerCase().endsWith('.'+extension));assert(row,extension);
  const preview=await rpc('preview',{assetId:row.id});assert.equal(preview.kind,extension);previews.push({assetId:row.id,name:row.name,...preview,path:path.join(cache,preview.file)});
  const converted=await rpc('export',{assetIds:[row.id],format:'png',output:path.join(output,'png')});assert.equal(converted.failed,0,JSON.stringify(converted.results));pngExports.push(...converted.results);
 }
 const archive=rows.find(r=>r.type==='AndroidPackage'),preview=await rpc('preview',{assetId:archive.id}),directory=JSON.parse(fs.readFileSync(path.join(cache,preview.file)));
 assert.equal(directory.memberCount,3139);assert.equal(directory.semanticComplete,false);assert(directory.members.every(m=>m.error===null));
 assert.equal(await hash(source),sourceSha256);
 const result={source,sourceSha256,entry,load,exported,previews,pngExports,archiveDirectory:directory,originalUnchanged:true,scope:'Complete native export of this ZIP member set; not full DB or Android semantic completeness'};
 fs.writeFileSync(path.join(output,'result.json'),JSON.stringify(result,null,2));fs.writeFileSync(path.join(base,'zip-worker-latest.json'),JSON.stringify({output,report:path.join(output,'result.json')}));console.log(JSON.stringify({output,nativeExports:exported.length,imageMembers:load.types.ImageFile,previewFormats:previews.map(r=>r.kind),originalUnchanged:true}));
}finally{worker.stdin.end();let timer;try{await Promise.race([exited,new Promise(resolve=>{timer=setTimeout(()=>{worker.kill();resolve();},3000)})]);}finally{clearTimeout(timer);}log.end();}})().catch(e=>{console.error(e);process.exitCode=1;});
