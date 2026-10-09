// Re-read already extracted, SHA-verified evidence payloads; never open game DBs.
const fs=require('node:fs'),path=require('node:path'),cp=require('node:child_process'),rl=require('node:readline'),crypto=require('node:crypto');
const [buildArg,rootArg]=process.argv.slice(2),build=path.resolve(buildArg),root=path.resolve(rootArg),base=path.join(root,'evidence');
const selections=JSON.parse(fs.readFileSync(path.join(base,'action-candidate-entries.json'))).map(x=>({...x,probe:path.join(base,'probes','actions',path.basename(path.dirname(x.source))+'-'+x.id)}));
for(const id of ['18748427691075818','913661504037930558','2575805863185491323'])selections.push({id,probe:path.join(base,'probes','9-'+id),kind:'StdrTable'});
const cache=path.join(root,'.work',crypto.randomUUID());fs.mkdirSync(cache,{recursive:true});
const child=cp.spawn(path.join(build,'worker/Hok.Worker.exe'),[cache],{cwd:path.join(build,'worker'),windowsHide:true});
const pending=new Map(),exited=new Promise(resolve=>child.once('exit',resolve));let serial=0;
const log=fs.createWriteStream(path.join(base,'structured-export.log'));child.stderr.pipe(log);
rl.createInterface({input:child.stdout}).on('line',line=>{const r=JSON.parse(line),p=pending.get(r.id);if(p){pending.delete(r.id);clearTimeout(p.timer);r.ok?p.resolve(r.data):p.reject(Error(r.error.message));}});
child.on('exit',code=>{for(const p of pending.values()){clearTimeout(p.timer);p.reject(Error('Worker exited '+code));}});
function rpc(method,payload){return new Promise((resolve,reject)=>{const id=String(++serial),timer=setTimeout(()=>{reject(Error('Timeout '+method));child.kill();},120000);pending.set(id,{resolve,reject,timer});child.stdin.write(JSON.stringify({id,method,payload})+'\n');});}
async function hash(file){const h=crypto.createHash('sha256');for await(const b of fs.createReadStream(file))h.update(b);return h.digest('hex').toUpperCase();}
(async()=>{const outputs=[];try{for(const selection of selections){
 const report=JSON.parse(fs.readFileSync(path.join(selection.probe,'report.json'))),file=path.join(selection.probe,selection.id+'.payload');
 if(await hash(file)!==report.sha256)throw Error('Extracted evidence hash changed: '+file);
 const loaded=await rpc('load',{paths:[file]}),rows=await rpc('list',{page:0});
 if(loaded.errorCount||rows.total!==1||rows.items[0].type!==selection.kind)throw Error('Unexpected structured detection: '+JSON.stringify({selection,loaded,rows}));
 const row=rows.items[0],out=path.join(root,'asset',path.basename(path.dirname(report.source)),path.basename(report.source,'.db'),selection.kind,selection.id),results=[];
 for(const format of ['original','json']){
  const exported=await rpc('export',{assetIds:[row.id],format,output:out});for(const item of exported.results){if(!item.ok)throw Error(JSON.stringify(item));results.push({...item,sha256:await hash(item.path)});}
 }
 outputs.push({source:report.source,entry:selection.id,payloadSha256:report.sha256,kind:selection.kind,results});
 fs.writeFileSync(path.join(base,'structured-exports.json'),JSON.stringify(outputs,null,2));console.log(JSON.stringify({entry:selection.id,kind:selection.kind,ok:true}));
 }}finally{child.stdin.end();await Promise.race([exited,new Promise(resolve=>setTimeout(()=>{child.kill();resolve();},3000))]);log.end();
 const absolute=path.resolve(cache),parent=path.join(root,'.work');if(!absolute.startsWith(parent+path.sep)||!/^[-0-9a-f]{36}$/i.test(path.basename(absolute)))throw Error('Unsafe cleanup');await fs.promises.rm(absolute,{recursive:true,force:true,maxRetries:5,retryDelay:100});}
})().catch(e=>{console.error(e);process.exitCode=1});
