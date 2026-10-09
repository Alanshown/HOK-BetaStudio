// Prepare bounded export jobs from an explicit supplemental missing-reference
// investigation. This does not promote a PathID match to confirmed identity.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const root=path.resolve(process.argv[2]),base=path.join(root,'evidence');
const candidates=JSON.parse(fs.readFileSync(path.join(base,'additional-cross-db-candidates.json')));
const graphs=['dependency-graph.json','additional-dependency-graph.json'].map(f=>JSON.parse(fs.readFileSync(path.join(base,f))));
const selected=new Set(graphs.flatMap(g=>g.nodes.map(n=>n.sourcePath+'|'+n.asset_id))),sources=new Map(),nodes=[];
for(const match of candidates.found){
 if(match.candidates.length!==1)continue;
 const row=match.candidates[0];if(selected.has(row.sourcePath+'|'+row.asset_id))continue;
 // Check the actual immutable JSONL byte span, not just a derived DB row.
 const b=Buffer.alloc(row.line_bytes),fd=fs.openSync(row.evidence,'r');let read=0;
 try{while(read<b.length){const n=fs.readSync(fd,b,read,b.length-read,row.line_offset+read);assert(n>0,'Evidence ended inside object');read+=n;}}finally{fs.closeSync(fd);}
 const object=JSON.parse(b);assert.equal(object.id,row.asset_id);assert.equal(object.pathId,row.path_id);assert.equal(object.sha256,row.sha256);assert.equal(object.type,row.type);
 const report=graphs.flatMap(g=>g.sources).find(s=>s.path===row.sourcePath);
 assert(report&&report.evidence===row.evidence,'Rebuild against the current source report before exporting');
 if(!sources.has(row.sourcePath))sources.set(row.sourcePath,{...report,id:sources.size+1});
 nodes.push({...row,id:nodes.length+1,source:sources.get(row.sourcePath).id,reasons:['cross-db-unique-PathID-candidate-from-additional-graph'],identityConfirmed:false});selected.add(row.sourcePath+'|'+row.asset_id);
}
const graph={schema:1,query:graphs[0].query,generated:new Date().toISOString(),indexedSources:sources.size,plannedSources:graphs[0].plannedSources,libraryComplete:false,semanticComplete:false,selectionOnly:true,identityPolicy:'Bounded export of exact byte-verified PathID candidates; references are not expanded and external GUID identities remain unverified.',sources:[...sources.values()],nodes,edges:[],reverse:[],counts:{},unexpandedDependencies:true};
const output=path.join(base,'probes','additional-cross-db-export-graph.json');fs.mkdirSync(path.dirname(output),{recursive:true});fs.writeFileSync(output,JSON.stringify(graph,null,2));console.log(JSON.stringify({output,sources:sources.size,objects:nodes.length,types:nodes.map(n=>n.type)}));
