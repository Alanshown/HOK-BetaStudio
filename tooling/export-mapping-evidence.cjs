// One source DB, typed nested metadata exports, independent record/hash checks.
const fs=require('node:fs'),path=require('node:path'),cp=require('node:child_process'),rl=require('node:readline'),crypto=require('node:crypto'),assert=require('node:assert/strict');
const[buildArg,rootArg]=process.argv.slice(2),build=path.resolve(buildArg),root=path.resolve(rootArg),base=path.join(root,'evidence');
const inventory=JSON.parse(fs.readFileSync(path.join(base,'inventory.json'))),source=path.join(inventory.input,'0','0_0.db'),cache=path.join(root,'.work',crypto.randomUUID());fs.mkdirSync(cache,{recursive:true});
const child=cp.spawn(path.join(build,'worker/Hok.Worker.exe'),[cache],{cwd:path.join(build,'worker'),windowsHide:true}),pending=new Map(),exited=new Promise(resolve=>child.once('exit',resolve));let serial=0;
const log=fs.createWriteStream(path.join(base,'mapping-export.log'));child.stderr.pipe(log);
rl.createInterface({input:child.stdout}).on('line',line=>{const r=JSON.parse(line),p=pending.get(r.id);if(p){pending.delete(r.id);clearTimeout(p.timer);r.ok?p.resolve(r.data):p.reject(Error(r.error.message));}});
child.once('exit',code=>{for(const p of pending.values()){clearTimeout(p.timer);p.reject(Error('Worker exited '+code));}});
const rpc=(method,payload)=>new Promise((resolve,reject)=>{const id=String(++serial),timer=setTimeout(()=>{child.kill();reject(Error('Timeout '+method));},10*60*1000);pending.set(id,{resolve,reject,timer});child.stdin.write(JSON.stringify({id,method,payload})+'\n');});
async function hash(file){const h=crypto.createHash('sha256');for await(const b of fs.createReadStream(file))h.update(b);return h.digest('hex');}
(async()=>{const outputs=[];try{
 const loaded=await rpc('load',{paths:[source]});assert.equal(loaded.errorCount,0,JSON.stringify(loaded.errors));
 for(const [type,entryId,count]of[['QtsResourceMap','6167850575042422745',299589],['QtsScriptDependencies','2254348891505130285',124602],['QtsTypeTreeDatabase','18078322517950175870',1398],['QtsKeyValueDatabase','8071403838198930324',36422]]){
  const rows=await rpc('list',{type,page:0});assert.equal(rows.total,1);const row=rows.items[0];assert(row.id.endsWith('/'+entryId));
  const results=[],output=path.join(root,'asset','0','0_0',type);
  for(const format of ['original','json']){const result=await rpc('export',{assetIds:[row.id],format,output}),r=result.results[0];assert(r.ok,JSON.stringify(r));results.push({...r,sha256:await hash(r.path)});}
  const original=results.find(r=>r.format==='original'),json=results.find(r=>r.format==='json');assert.equal(original.sha256,await hash(path.join(base,'probes','nested-'+entryId,entryId+'.payload')));
  const decoded=JSON.parse(fs.readFileSync(json.path));assert.equal(decoded.recordCount??decoded.schemaCount,count);assert.equal(decoded.truncated,false);
  if(type==='QtsScriptDependencies')assert.equal(decoded.records.length+decoded.uninterpretedRecords.length,count);
  else assert.equal((decoded.records??decoded.schemas).length,count);
  outputs.push({source,entryId,type,name:row.name,count,results,originalHashVerified:true,allRecordsExported:true});fs.writeFileSync(path.join(base,'mapping-exports.json'),JSON.stringify(outputs,null,2));console.log(JSON.stringify({type,count,originalHashVerified:true,allRecordsExported:true}));
 }
 }finally{child.stdin.end();await Promise.race([exited,new Promise(resolve=>setTimeout(()=>{child.kill();resolve();},3000))]);log.end();const absolute=path.resolve(cache),parent=path.join(root,'.work');if(!absolute.startsWith(parent+path.sep)||!/^[-0-9a-f]{36}$/i.test(path.basename(absolute)))throw Error('Unsafe cache path');await fs.promises.rm(absolute,{recursive:true,force:true,maxRetries:5,retryDelay:100});}
})().catch(e=>{console.error(e);process.exitCode=1});
