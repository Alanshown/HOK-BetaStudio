// Promote a reviewed full-corpus graph without silently losing prior selected
// objects. Retain every previous graph as a recovery/evidence snapshot.
const fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto'),assert=require('node:assert/strict');
const [rootArg,candidateArg]=process.argv.slice(2),base=path.resolve(rootArg,'evidence'),candidate=path.resolve(candidateArg),target=path.join(base,'dependency-graph.json');
assert(candidate.startsWith(base+path.sep)&&candidate!==target,'Candidate must be a separate evidence artifact');
const read=f=>JSON.parse(fs.readFileSync(f)),inventory=read(path.join(base,'inventory.json')),graph=read(candidate),selected=new Set(graph.nodes.map(n=>n.sourcePath+'|'+n.asset_id));
assert.equal(selected.size,graph.nodes.length,'Duplicate selected identities');assert.equal(graph.indexedSources,inventory.files.length);assert.equal(graph.semanticComplete,false,'This operation cannot attest runtime completeness');
const sources=new Map(graph.sources.map(s=>[s.path,s]));assert.equal(sources.size,inventory.files.length);
for(const source of inventory.files){
 const entry=sources.get(source);assert(entry&&entry.complete===1,'Incomplete/missing source: '+source);
 const key=crypto.createHash('sha256').update(source.toLowerCase()).digest('hex').slice(0,24),report=read(path.join(base,'db',key+'.report.json'));
 assert.equal(entry.evidence,report.output,'Graph uses a stale scan: '+source);assert.equal(entry.sha256.toLowerCase(),report.indexed.sourceSha256.toLowerCase());
}
const oldFiles=['dependency-graph.json','additional-dependency-graph.json','probes/additional-cross-db-export-graph.json'].map(f=>path.join(base,f)).filter(f=>fs.existsSync(f));
for(const file of oldFiles)for(const node of read(file).nodes)assert(selected.has(node.sourcePath+'|'+node.asset_id),'Previously selected object disappeared; review required: '+node.asset_id);
const stamp=Date.now()+'-'+crypto.randomUUID(),archive=path.join(base,'probes','graph-history',stamp);fs.mkdirSync(archive,{recursive:true});
const backups=oldFiles.map(file=>{const saved=path.join(archive,path.basename(file));fs.copyFileSync(file,saved,fs.constants.COPYFILE_EXCL);return{file,saved};});
const temporary=target+'.'+stamp+'.tmp';fs.copyFileSync(candidate,temporary,fs.constants.COPYFILE_EXCL);fs.renameSync(temporary,target);
// Supplemental graphs are now strict subsets of the fresh main graph. Move
// them aside only after the new main graph and all backups exist.
for(const file of oldFiles.filter(f=>f!==target))fs.renameSync(file,path.join(archive,'superseded-'+path.basename(file)));
const result={generated:new Date().toISOString(),candidate,target,indexedSources:graph.indexedSources,selectedObjects:graph.nodes.length,allPriorSelectionsRetained:true,backups,semanticComplete:false};
fs.writeFileSync(path.join(archive,'promotion.json'),JSON.stringify(result,null,2));console.log(JSON.stringify(result));
