// Regression for the actual 23 managed-reference blueprints in one supplied DB.
const fs=require('node:fs'),path=require('node:path'),cp=require('node:child_process'),rl=require('node:readline'),crypto=require('node:crypto'),assert=require('node:assert/strict');
const [buildArg,rootArg]=process.argv.slice(2),build=path.resolve(buildArg),root=path.resolve(rootArg),base=path.join(root,'evidence');
const hashes=new Set(['D78DA72FD1A404FE9257B1D6F6579AA7','67E0C4D2E1058CA7799983CA1F725F59']);
async function hash(file){const h=crypto.createHash('sha256');for await(const b of fs.createReadStream(file))h.update(b);return h.digest('hex');}
(async()=>{
 const reports=fs.readdirSync(path.join(base,'db')).filter(f=>f.endsWith('.report.json')).map(f=>JSON.parse(fs.readFileSync(path.join(base,'db',f))));
 const report=reports.find(r=>path.basename(r.source)==='305000000146_0.db');assert(report?.ok);
 const selected=[];for await(const line of rl.createInterface({input:fs.createReadStream(report.output),crlfDelay:Infinity}))if(line.includes('"kind":"object"')){const r=JSON.parse(line);if(hashes.has(r.typeHash))selected.push(r);}
 assert.equal(selected.length,23);assert.equal(await hash(report.source),report.indexed.sourceSha256.toLowerCase());
 const output=path.join(base,'probes','managed-worker-'+Date.now()),cache=path.resolve('.cache','managed-worker-'+crypto.randomUUID());fs.mkdirSync(cache,{recursive:true});fs.mkdirSync(output,{recursive:true});
 const child=cp.spawn(path.join(build,'worker/Hok.Worker.exe'),[cache],{cwd:path.join(build,'worker'),windowsHide:true}),log=fs.createWriteStream(path.join(output,'worker.log'));child.stderr.pipe(log);
 const exited=new Promise(resolve=>child.once('exit',resolve)),pending=new Map();let serial=0;
 rl.createInterface({input:child.stdout}).on('line',line=>{const r=JSON.parse(line),p=pending.get(r.id);if(p){clearTimeout(p.timer);pending.delete(r.id);r.ok?p.resolve(r.data):p.reject(Error(r.error.message));}});
 child.on('exit',code=>{for(const p of pending.values()){clearTimeout(p.timer);p.reject(Error('Worker exited '+code));}pending.clear();});
 const rpc=(method,payload)=>new Promise((resolve,reject)=>{const id=String(++serial),timer=setTimeout(()=>{pending.delete(id);reject(Error('Timeout '+method));child.kill();},5*60*1000);pending.set(id,{resolve,reject,timer});child.stdin.write(JSON.stringify({id,method,payload})+'\n');});
 try{
  const entries=[...new Set(selected.map(r=>r.entry))],load=await rpc('load',{paths:[report.source],typeSchemaPaths:[path.join(base,'probes/nested-18078322517950175870/18078322517950175870.payload')],qtsEntrySelection:{[report.source]:entries}});
  assert.equal(load.errorCount,0,JSON.stringify(load.errors));const rows=[];
  for(let page=0;;page++){const list=await rpc('list',{page});rows.push(...list.items);if((page+1)*200>=list.total)break;}
  const exports=[];for(const expected of selected){
   const row=rows.find(r=>r.id===expected.id);assert(row,expected.id);assert.equal(row.parseStatus,'typed-complete',JSON.stringify(row));assert.equal(row.remainingBytes,0);
   const preview=await rpc('dump',{assetId:row.id});assert(preview);
   for(const format of ['json','raw']){
    const result=await rpc('export',{assetIds:[row.id],format,output:path.join(output,format)});assert.equal(result.failed,0,JSON.stringify(result));
    const item=result.results[0];assert(item.ok);if(format==='raw')assert.equal(await hash(item.path),expected.sha256.toLowerCase());else JSON.parse(fs.readFileSync(item.path));
    exports.push({...item,sha256:await hash(item.path)});
   }
  }
  assert.equal(await hash(report.source),report.indexed.sourceSha256.toLowerCase());
  const result={source:report.source,sourceSha256:report.indexed.sourceSha256,load,selected:23,allTypedComplete:true,allOriginalObjectHashesMatch:true,sourceUnchanged:true,exports};
  fs.writeFileSync(path.join(output,'result.json'),JSON.stringify(result,null,2));console.log(JSON.stringify({...result,exports:exports.length,output}));
 }finally{child.stdin.end();await Promise.race([exited,new Promise(resolve=>setTimeout(()=>{child.kill();resolve();},3000))]);log.end();}
})().catch(e=>{console.error(e);process.exitCode=1;});
