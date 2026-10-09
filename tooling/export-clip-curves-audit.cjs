// Matched-context baseline for a decoder-only comparison, one source per worker.
const fs=require('node:fs'),path=require('node:path'),cp=require('node:child_process'),rl=require('node:readline'),crypto=require('node:crypto'),assert=require('node:assert/strict');
const [buildArg,contextArg,outputArg,schemaArg]=process.argv.slice(2),build=path.resolve(buildArg),context=path.resolve(contextArg),output=path.resolve(outputArg),base=path.join(output,'evidence');
const graph=JSON.parse(fs.readFileSync(path.join(context,'evidence/dependency-graph.json'))),inventory=JSON.parse(fs.readFileSync(path.join(context,'evidence/inventory.json'))),nodes=graph.nodes.filter(n=>n.type==='AnimationClip'&&path.basename(n.sourcePath)==='300100014000_0.db');
assert.equal(nodes.length,62);const sources=[...new Set(nodes.map(n=>n.sourcePath))];assert.equal(sources.length,1);
fs.mkdirSync(base,{recursive:true});const child=cp.spawn(path.join(build,'worker/Hok.Worker.exe'),[path.join(output,'cache')],{cwd:path.join(build,'worker'),windowsHide:true});
const log=fs.createWriteStream(path.join(base,'worker.log'));child.stderr.pipe(log);let serial=0;const pending=new Map(),exited=new Promise(resolve=>child.once('exit',resolve));
rl.createInterface({input:child.stdout}).on('line',line=>{const r=JSON.parse(line),p=pending.get(r.id);if(p){pending.delete(r.id);clearTimeout(p.timer);r.ok?p.resolve(r.data):p.reject(Error(r.error.message));}});
child.on('exit',code=>{for(const p of pending.values()){clearTimeout(p.timer);p.reject(Error('Worker exited '+code));}pending.clear();});
const rpc=(method,payload)=>new Promise((resolve,reject)=>{const id=String(++serial),timer=setTimeout(()=>{pending.delete(id);child.kill();reject(Error('Timeout '+method));},600000);pending.set(id,{resolve,reject,timer});child.stdin.write(JSON.stringify({id,method,payload})+'\n');});
async function hash(f){const h=crypto.createHash('sha256');for await(const b of fs.createReadStream(f))h.update(b);return h.digest('hex');}
(async()=>{try{
 const source=sources[0];assert.equal(await hash(source),graph.sources.find(s=>s.path===source).sha256.toLowerCase());
 const load=await rpc('load',{paths:[source],dependencyPaths:inventory.files,typeSchemaPaths:[path.resolve(schemaArg)]});assert.equal(load.errorCount,0,JSON.stringify(load.errors));
 fs.writeFileSync(path.join(base,'load.json'),JSON.stringify(load,null,2));const results=[];
 for(let i=0;i<nodes.length;i+=20){const batch=nodes.slice(i,i+20),result=await rpc('export',{assetIds:batch.map(n=>n.asset_id),format:'curves-json',output:path.join(output,'asset')});assert.equal(result.failed,0,JSON.stringify(result));
  for(const r of result.results){const n=batch.find(n=>n.asset_id===r.id);assert(r.ok&&!r.rawFallback);results.push({...r,source,pathId:n.path_id,name:n.name,type:'AnimationClip',sha256:await hash(r.path)});}
  console.log(JSON.stringify({exported:results.length,total:nodes.length}));
 }
 fs.writeFileSync(path.join(base,'asset-exports.jsonl'),results.map(r=>JSON.stringify(r)).join('\n')+'\n');
}finally{child.stdin.end();let timer;await Promise.race([exited,new Promise(resolve=>timer=setTimeout(()=>{child.kill();resolve();},3000))]);clearTimeout(timer);log.end();}})().catch(e=>{console.error(e);process.exitCode=1;});
