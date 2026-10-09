// Incremental collection after graph expansion. Keep failed attempts visible;
// this command is deliberately not a retry/revalidation of old exports.
const fs=require('node:fs'),path=require('node:path'),cp=require('node:child_process');
const [buildArg,rootArg,schemaArg]=process.argv.slice(2);
if(!schemaArg)throw Error('Usage: export-unattempted-evidence <build> <output> <verified-TTre>');
const root=path.resolve(rootArg),base=path.join(root,'evidence'),graphFile=process.env.HOK_EVIDENCE_GRAPH?path.resolve(process.env.HOK_EVIDENCE_GRAPH):path.join(base,'dependency-graph.json'),graph=JSON.parse(fs.readFileSync(graphFile)),attempted=new Set();
for(const line of fs.readFileSync(path.join(base,'asset-exports.jsonl'),'utf8').split(/\r?\n/).filter(Boolean)){
 const row=JSON.parse(line);attempted.add(row.source+'|'+row.id);
}
const groups=new Map();for(const node of graph.nodes){
 if(attempted.has(node.sourcePath+'|'+node.asset_id))continue;
 if(!groups.has(node.sourcePath))groups.set(node.sourcePath,[]);groups.get(node.sourcePath).push(node.asset_id);
}
(async()=>{
 if(process.env.HOK_EVIDENCE_EXPORT_PLAN==='1'){
  console.log(JSON.stringify({graph:graphFile,sources:[...groups].map(([source,ids])=>({source,ids})),newObjects:[...groups.values()].reduce((n,ids)=>n+ids.length,0),readOnlyPlan:true}));return;
 }
 for(const[source,ids]of groups){
  // Exporter loads the complete source selection for related rig context, but
  // only these explicit newly selected identities are written.
  console.log(JSON.stringify({source,newlySelectedObjects:ids.length,mode:'unattempted-only'}));
  const child=cp.spawn(process.execPath,[path.join(__dirname,'export-evidence-selection.cjs'),path.resolve(buildArg),root,path.basename(source),path.resolve(schemaArg),'',ids.join('|')],{stdio:'inherit',windowsHide:true});
  const code=await new Promise((resolve,reject)=>{child.once('error',reject);child.once('exit',resolve);});
  if(code!==0)throw Error('Selection export failed: '+source+' exit '+code);
 }
 console.log(JSON.stringify({sources:groups.size,attemptedNewObjects:[...groups.values()].reduce((sum,ids)=>sum+ids.length,0),scope:'New object identities only; previous failures and validation gaps are unchanged.'}));
})().catch(e=>{console.error(e);process.exitCode=1;});
