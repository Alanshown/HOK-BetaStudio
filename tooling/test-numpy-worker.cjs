const fs=require('node:fs'),path=require('node:path'),cp=require('node:child_process'),rl=require('node:readline'),crypto=require('node:crypto'),assert=require('node:assert/strict');
const [buildArg,sourceArg,baseArg]=process.argv.slice(2),build=path.resolve(buildArg),source=path.resolve(sourceArg),base=path.resolve(baseArg);
const entries=['1779253150636880162','3861408521056308517','13727255725002482632'],output=path.join(base,'probes','numpy-worker-'+Date.now()),cache=path.join(output,'cache');fs.mkdirSync(cache,{recursive:true});
const child=cp.spawn(path.join(build,'worker/Hok.Worker.exe'),[cache],{cwd:path.join(build,'worker'),windowsHide:true}),log=fs.createWriteStream(path.join(output,'worker.log'));child.stderr.pipe(log);const pending=new Map();let serial=0;
rl.createInterface({input:child.stdout}).on('line',line=>{const r=JSON.parse(line),p=pending.get(r.id);if(p){pending.delete(r.id);r.ok?p.resolve(r.data):p.reject(Error(r.error.message));}});
child.on('exit',code=>{for(const p of pending.values())p.reject(Error('Worker exited '+code));pending.clear();});
const rpc=(method,payload)=>new Promise((resolve,reject)=>{const id=String(++serial);pending.set(id,{resolve,reject});child.stdin.write(JSON.stringify({id,method,payload})+'\n');});
async function hash(file){const h=crypto.createHash('sha256');for await(const b of fs.createReadStream(file))h.update(b);return h.digest('hex');}
(async()=>{try{
 const before=await hash(source),load=await rpc('load',{paths:[source],qtsEntrySelection:{[source]:entries}});
 assert.equal(load.errorCount,0,JSON.stringify(load.errors));assert.equal(load.types.NumpyArchive,3);assert.equal(load.types.NumpyArray,243);assert.equal(load.materializedResourceBytes,0);
 const rows=[];for(let page=0;;page++){const list=await rpc('list',{page});rows.push(...list.items);if((page+1)*200>=list.total)break;}
 const sample=rows.find(r=>r.type==='NumpyArray'),preview=await rpc('preview',{assetId:sample.id}),data=JSON.parse(fs.readFileSync(path.join(cache,preview.file)));
 assert(data.header.elements>0);assert(data.sampleValues.length<=64);assert.equal(data.truncated,data.header.elements>data.sampleValues.length);
 const exported=[];for(let i=0;i<rows.length;i+=20){const r=await rpc('export',{assetIds:rows.slice(i,i+20).map(r=>r.id),format:'original',output:path.join(output,'exports')});assert.equal(r.failed,0,JSON.stringify(r.results));for(const item of r.results)exported.push({...item,...rows.find(r=>r.id===item.id)});}
 assert.equal(await hash(source),before);const result={source,sourceSha256:before,load,preview:data,exported,originalUnchanged:true,scope:'Selected NumPy containers only; not full DB semantic coverage'};
 fs.writeFileSync(path.join(output,'result.json'),JSON.stringify(result,null,2));console.log(JSON.stringify({output,archives:3,arrays:243,exports:exported.length,originalUnchanged:true}));
}finally{child.stdin.end();log.end();}})().catch(e=>{console.error(e);child.kill();process.exitCode=1});
