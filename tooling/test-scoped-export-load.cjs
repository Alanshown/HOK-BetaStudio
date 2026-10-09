// Compare an explicitly scoped export load with full-scan object evidence.
const fs=require('node:fs'),path=require('node:path'),cp=require('node:child_process'),rl=require('node:readline'),crypto=require('node:crypto'),assert=require('node:assert/strict');
const [buildArg,rootArg,sourceFilter,schemaPath]=process.argv.slice(2),build=path.resolve(buildArg),root=path.resolve(rootArg),base=path.join(root,'evidence');
const graph=JSON.parse(fs.readFileSync(process.env.HOK_EVIDENCE_GRAPH?path.resolve(process.env.HOK_EVIDENCE_GRAPH):path.join(base,'dependency-graph.json'))),inventory=JSON.parse(fs.readFileSync(path.join(base,'inventory.json')));
const nodes=graph.nodes.filter(n=>path.basename(n.sourcePath)===sourceFilter),sources=[...new Set(nodes.map(n=>n.sourcePath))];assert.equal(sources.length,1);
const scratch=path.resolve('.cache','scoped-check-'+crypto.randomUUID());fs.mkdirSync(scratch,{recursive:true});
const child=cp.spawn(path.join(build,'worker/Hok.Worker.exe'),[scratch],{cwd:path.join(build,'worker'),windowsHide:true}),log=fs.createWriteStream(path.join(scratch,'worker.log'));child.stderr.pipe(log);
const pending=new Map();let serial=0;const exited=new Promise(resolve=>child.once('exit',resolve));
rl.createInterface({input:child.stdout}).on('line',line=>{const r=JSON.parse(line),p=pending.get(r.id);if(p){clearTimeout(p.timer);pending.delete(r.id);r.ok?p.resolve(r.data):p.reject(Error(r.error.message));}});
child.on('exit',code=>{for(const p of pending.values()){clearTimeout(p.timer);p.reject(Error('Worker exited '+code));}pending.clear();});
const rpc=(method,payload)=>new Promise((resolve,reject)=>{const id=String(++serial),timer=setTimeout(()=>{pending.delete(id);reject(Error('Timeout '+method));child.kill();},5*60*1000);pending.set(id,{resolve,reject,timer});child.stdin.write(JSON.stringify({id,method,payload})+'\n');});
(async()=>{try{
 const source=sources[0],entries=[...new Set(nodes.map(n=>n.entry))];
 const load=await rpc('load',{paths:[source],dependencyPaths:inventory.files,typeSchemaPaths:[path.resolve(schemaPath)],qtsEntrySelection:{[source]:entries}});assert.equal(load.errorCount,0);assert.equal(load.scopedEntryLoad,true);
 const confirmed=[];
 for(let at=0;at<nodes.length;at+=20){const batch=nodes.slice(at,at+20),result=await rpc('export',{assetIds:batch.map(n=>n.asset_id),format:'raw',output:path.join(scratch,'raw-equality')});assert.equal(result.failed,0);
  for(const r of result.results){const expected=batch.find(n=>n.asset_id===r.id);const actual=crypto.createHash('sha256').update(fs.readFileSync(r.path)).digest('hex');assert.equal(actual.toLowerCase(),expected.sha256.toLowerCase(),'Source object bytes changed: '+r.id);confirmed.push(r.id);}
 }
 for(const method of ['evidenceIndex','rawAudit','replacementState']){let rejected=false;try{await rpc(method,{output:path.join(scratch,'forbidden-'+method)});}catch(e){rejected=/Scoped|Load a package first/.test(e.message);}assert(rejected,method+' must not treat a partial export load as a complete package');}
 const result={source,selectedEntries:entries.length,selectedObjects:nodes.length,allOriginalObjectHashesMatch:true,verifiedObjects:confirmed.length,fullCoverageOperationsRejected:true,load,scratch};
 fs.writeFileSync(path.join(base,'scoped-export-regression-'+crypto.createHash('sha256').update(sourceFilter).digest('hex').slice(0,16)+'.json'),JSON.stringify(result,null,2));console.log(JSON.stringify({source,selectedEntries:entries.length,selectedObjects:nodes.length,loadedObjects:load.unityObjects,retainedManagedBytes:load.retainedManagedBytes,allOriginalObjectHashesMatch:true,fullCoverageOperationsRejected:true}));
}finally{child.stdin.end();await Promise.race([exited,new Promise(resolve=>setTimeout(()=>{child.kill();resolve();},3000))]);log.end();}})().catch(e=>{console.error(e);child.kill();process.exitCode=1;});
