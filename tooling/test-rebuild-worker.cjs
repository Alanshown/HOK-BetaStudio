const fs=require('node:fs'),path=require('node:path'),cp=require('node:child_process'),readline=require('node:readline'),assert=require('node:assert/strict'),crypto=require('node:crypto');
const root=path.resolve(__dirname,'..'),build=process.env.HOK_BUILD_DIR||'build/HOK-BetaStudio-1.2-rebuild-beta-win-x64',exe=path.join(root,build,'worker/Hok.Worker.exe');
const temp=fs.mkdtempSync(path.join(root,'.cache','rebuild-worker-test-')),sha=b=>crypto.createHash('sha256').update(b).digest('hex');
const folder=path.join(root,'3200010500'),inputs=fs.readdirSync(folder).filter(n=>n.endsWith('.db')).map(n=>path.join(folder,n));
const allHashes=()=>Object.fromEntries(fs.readdirSync(folder).map(n=>[n,sha(fs.readFileSync(path.join(folder,n)))]));
function worker(){
 const proc=cp.spawn(exe,[path.join(temp,crypto.randomUUID())],{cwd:path.dirname(exe),windowsHide:true,stdio:['pipe','pipe','pipe'],env:{...process.env,DOTNET_ROOT:path.join(root,'.tools/dotnet')}}),pending=new Map();let serial=0;
 proc.stderr.resume();readline.createInterface({input:proc.stdout}).on('line',line=>{const r=JSON.parse(line),p=pending.get(r.id);if(p){clearTimeout(p.timer);pending.delete(r.id);r.ok?p.resolve(r.data):p.reject(Error(r.error.message))}});
 return {proc,rpc:(method,payload={})=>new Promise((resolve,reject)=>{const id=String(++serial),timer=setTimeout(()=>{pending.delete(id);reject(Error('worker timeout'))},120000);pending.set(id,{resolve,reject,timer});proc.stdin.write(JSON.stringify({id,method,payload})+'\n')})};
}
async function main(){const a=worker(),b=worker(),results=[],before=allHashes();
try{
 const loaded=await a.rpc('load',{paths:inputs});assert.equal(loaded.count,841);assert.equal((await a.rpc('replacementState')).count,0);
 await assert.rejects(a.rpc('rebuild',{output:temp}));results.push('rebuild rejected without any staged replacement');
 const rows=(await a.rpc('list',{type:'ResourceFile'})).items,changes=[];
 for(const asset of rows.slice(0,2)){
  const exported=await a.rpc('export',{assetIds:[asset.id],format:'original',output:temp});assert.equal(exported.success,1);
  const original=fs.readFileSync(exported.results[0].path);assert.equal(original.length%16,0);
  // Reuse an actual ASTC block from the source, retaining its native block layout.
  const replacement=Buffer.alloc(original.length);for(let p=0;p<replacement.length;p+=16)original.copy(replacement,p,0,16);
  assert.notEqual(sha(original),sha(replacement));const file=path.join(temp,asset.pathId+'.bin');fs.writeFileSync(file,replacement);
  const staged=await a.rpc('replace',{assetId:asset.id,path:file});changes.push({asset,original,replacement,file});assert.equal(staged.count,changes.length);
 }
 results.push('two real native texture streams staged after exact compressed-slot and reference validation');
 const first=changes[0];const version2=Buffer.alloc(first.original.length);for(let p=0;p<version2.length;p+=16)first.original.copy(version2,p,16,32);fs.writeFileSync(first.file,version2);first.replacement=version2;
 assert.equal((await a.rpc('replace',{assetId:first.asset.id,path:first.file})).count,2);results.push('same-item replacement updates its staged version without duplicating the item');
 const bad=path.join(temp,'wrong-length.bin');fs.writeFileSync(bad,Buffer.alloc(3));await assert.rejects(a.rpc('replace',{assetId:first.asset.id,path:bad}));assert.equal((await a.rpc('replacementState')).count,2);results.push('invalid repeated replacement rejected; previous staged versions remain');
 const tooLarge=path.join(temp,'does-not-fit.bin');fs.writeFileSync(tooLarge,crypto.randomBytes(first.original.length));await assert.rejects(a.rpc('replace',{assetId:first.asset.id,path:tooLarge}),/cannot fit/);assert.equal((await a.rpc('replacementState')).count,2);results.push('equal-length data that cannot fit a compressed slot is rejected without losing staged changes');
 const mono=(await a.rpc('list',{type:'MonoBehaviour'})).items[0];await assert.rejects(a.rpc('replace',{assetId:mono.id,path:bad}));results.push('opaque custom object replacement safely rejected');
 await assert.rejects(a.rpc('rebuild',{output:folder}),/separate|non-overlapping/);assert.deepEqual(allHashes(),before);results.push('rebuild into the original package directory rejected without touching its files');
 const one=await a.rpc('rebuild',{output:temp}),two=await a.rpc('rebuild',{output:temp});assert.notEqual(one.output,two.output);
 const hashes=dir=>Object.fromEntries(fs.readdirSync(dir).map(n=>[n,sha(fs.readFileSync(path.join(dir,n)))]));assert.deepEqual(hashes(one.output),hashes(two.output));results.push('two modified complete-package builds byte-identical; unique output folders prevent overwrite');
 assert.deepEqual(fs.readdirSync(one.output).sort(),fs.readdirSync(folder).sort());assert.equal(hashes(one.output)['record.bytes'],before['record.bytes']);assert.equal(hashes(one.output)['3200010500.db'],before['3200010500.db']);assert.notEqual(hashes(one.output)['3200010500_0.db'],before['3200010500_0.db']);results.push('all original companion files retained; only intended shard changes');
 const layout=JSON.parse(fs.readFileSync(path.join(root,'planning/mapping-audit-report.json'))).qts.find(q=>q.file==='3200010500_0.db'),oldShard=fs.readFileSync(path.join(folder,layout.file)),newShard=fs.readFileSync(path.join(one.output,layout.file)),allowed=Buffer.alloc(oldShard.length);
 for(const changed of changes)for(const chunk of layout.entries.find(e=>e.id===changed.asset.pathId).chunks)allowed.fill(1,chunk.offset,chunk.offset+chunk.compressed);
 assert.equal(oldShard.length,newShard.length);for(let i=0;i<oldShard.length;i++)if(!allowed[i])assert.equal(newShard[i],oldShard[i],'unexpected byte change at '+i);results.push('every stored byte outside the selected compressed slots remains identical, including unknown header fields');
 assert.equal((await b.rpc('load',{paths:fs.readdirSync(one.output).filter(n=>n.endsWith('.db')).map(n=>path.join(one.output,n))})).count,841);
 const rebuiltRows=(await b.rpc('list',{type:'ResourceFile'})).items;
 for(const changed of changes){const item=rebuiltRows.find(x=>x.pathId===changed.asset.pathId);assert(item);const result=await b.rpc('export',{assetIds:[item.id],format:'original',output:temp});assert.equal(sha(fs.readFileSync(result.results[0].path)),sha(changed.replacement));}
 results.push('fresh worker reopens 841 rows and exports the exact latest staged bytes');
 const textures=(await b.rpc('list',{type:'Texture2D'})).items;
 for(const name of ['10500_LianPo_Face_N_Show','10500_LianPo_Face_D_Show']){const texture=textures.find(t=>t.name===name);assert(texture);const preview=await b.rpc('preview',{assetId:texture.id});assert.equal(preview.kind,'png');assert(preview.size>64);}
 results.push('rebuilt native texture streams still decode to PNG previews in a fresh worker');
 const report=JSON.parse(fs.readFileSync(one.reportPath));assert.equal(report.gameCompatibilityVerified,false);assert.equal(report.beta,true);assert.equal(report.recommended,false);results.push('report explicitly records experimental status and unverified game compatibility');
 assert.deepEqual(allHashes(),before);results.push('all original DB and record.bytes hashes unchanged');
 await a.rpc('load',{paths:inputs});assert.equal((await a.rpc('replacementState')).count,0);results.push('new load clears staged replacements');
 const result={date:new Date().toISOString(),passed:results.length,results,build,output:temp,rebuild:one,fixtures:changes.map(x=>({assetId:x.asset.id,name:x.asset.name,pathId:x.asset.pathId,file:x.file,bytes:x.replacement.length}))};
 fs.writeFileSync(path.join(root,'planning/rebuild-worker-test-report.json'),JSON.stringify(result,null,2));console.log(JSON.stringify(result));
}finally{a.proc.kill();b.proc.kill();}}
main().catch(e=>{console.error(e);process.exitCode=1});
