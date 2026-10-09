const fs=require('node:fs'),path=require('node:path'),cp=require('node:child_process'),rl=require('node:readline'),crypto=require('node:crypto');
const [buildArg,sourceArg,outputArg,query='14006',schemaPath,mode]=process.argv.slice(2);
if(!outputArg)throw Error('Usage: evidence-session <build> <one-db> <output> [query]');
const build=path.resolve(buildArg),source=path.resolve(sourceArg),output=path.resolve(outputArg),cache=path.join(output,'.cache-'+crypto.randomUUID());
fs.mkdirSync(output,{recursive:true});const child=cp.spawn(path.join(build,'worker/Hok.Worker.exe'),[cache],{cwd:path.join(build,'worker'),windowsHide:true});
const log=fs.createWriteStream(path.join(output,'worker.log'));child.stderr.pipe(log);const pending=new Map();let serial=0;
rl.createInterface({input:child.stdout}).on('line',line=>{const r=JSON.parse(line),p=pending.get(r.id);if(p){pending.delete(r.id);r.ok?p.resolve(r.data):p.reject(Error(r.error.message));}});
const rpc=(method,payload)=>new Promise((resolve,reject)=>{let id=String(++serial);pending.set(id,{resolve,reject});child.stdin.write(JSON.stringify({id,method,payload})+'\n')});
child.on('exit',code=>{for(const p of pending.values())p.reject(Error('Worker exited '+code));pending.clear();});
const save=(name,value)=>fs.writeFileSync(path.join(output,name),JSON.stringify(value,null,2));
(async()=>{try{
 const load=await rpc('load',{paths:[source],typeSchemaPaths:schemaPath?[path.resolve(schemaPath)]:[]});save('load.json',load);console.log(JSON.stringify({source,count:load.count,errors:load.errors,types:load.types}));
 const indexed=await rpc('evidenceIndex',{output:path.join(output,'objects.jsonl'),query,deep:mode==='deep'});save('index.json',indexed);console.log(JSON.stringify(indexed));
 const matches=await rpc('list',{query,page:0});save('matches.json',matches);
 for(const row of matches.items.filter(r=>r.type==='MonoScript'))save(row.pathId+'.json',await rpc('dump',{assetId:row.id}));
 const volumes=await rpc('list',{type:'Unknown (90001)',page:0});save('volumes.json',volumes);
 }finally{child.stdin.end();log.end();}
})().catch(e=>{console.error(e);child.kill();process.exitCode=1});
