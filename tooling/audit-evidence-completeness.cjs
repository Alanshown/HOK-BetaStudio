// Acceptance accounting only: never infer completeness from a successful RPC.
const fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto');
const root=path.resolve(process.argv[2]),base=path.join(root,'evidence'),inventory=JSON.parse(fs.readFileSync(path.join(base,'inventory.json')));
const summary={skin:inventory.query,generated:new Date().toISOString(),deliverable:false,planned:inventory.files.length,completedReports:0,readFailures:[],missing:[],staleSources:[],revisions:{},objects:0,statuses:{},entryIssues:0,exportFailures:[],exportsWithWarnings:[],successfulExports:0,verifiedCopiedFiles:0,blockers:[]};
const reports=new Map();
for(const source of inventory.files){const key=crypto.createHash('sha256').update(source.toLowerCase()).digest('hex').slice(0,24),file=path.join(base,'db',key+'.report.json');
 if(!fs.existsSync(file)){summary.missing.push(source);continue;}const r=JSON.parse(fs.readFileSync(file));reports.set(source,r);
 if(!r.ok){summary.readFailures.push({source,error:r.error});continue;}
 if(!fs.existsSync(r.output)){summary.missing.push(source);continue;}
 const stat=fs.statSync(source);if(stat.size!==r.bytes||stat.mtimeMs!==r.mtimeMs)summary.staleSources.push(source);
 summary.completedReports++;const rev=r.schemaRevision??'legacy-without-schema-revision';summary.revisions[rev]=(summary.revisions[rev]??0)+1;
 summary.objects+=r.indexed?.objects??0;summary.entryIssues+=r.indexed?.entryIssues??0;
 if(r.load?.errorCount||r.indexed?.errors||r.indexed?.entryIssues)summary.readFailures.push({source,loadErrors:r.load?.errorCount,evidenceErrors:r.indexed?.errors,entryIssues:r.indexed?.entryIssues});
 for(const[k,n]of Object.entries(r.indexed?.statuses??{}))summary.statuses[k]=(summary.statuses[k]??0)+n;
}
const exportFile=path.join(base,'asset-exports.jsonl'),exportResults=new Map();if(fs.existsSync(exportFile))for(const line of fs.readFileSync(exportFile,'utf8').split(/\r?\n/).filter(Boolean)){const r=JSON.parse(line);exportResults.set(r.source+'|'+r.id+'|'+r.format,r);}
for(const r of exportResults.values()){if(!r.ok||r.rawFallback||!fs.existsSync(r.path??''))summary.exportFailures.push(r);else{summary.successfulExports++;if(r.warnings?.length)summary.exportsWithWarnings.push(r);}}
const copyFile=path.join(base,'copied-packages.json');if(fs.existsSync(copyFile))summary.verifiedCopiedFiles=JSON.parse(fs.readFileSync(copyFile)).records.length;
if(summary.missing.length)summary.blockers.push('Unscanned DBs: '+summary.missing.length);
if(summary.readFailures.length)summary.blockers.push('Failed or incomplete source reports: '+summary.readFailures.length);
if(summary.staleSources.length)summary.blockers.push('Source changed after scan: '+summary.staleSources.length);
if(Object.keys(summary.revisions).length!==1||summary.revisions['legacy-without-schema-revision'])summary.blockers.push('Mixed or missing parser/schema revisions; affected sources require re-indexing');
for(const[k,n]of Object.entries(summary.statuses))if(k!=='typed-complete')summary.blockers.push(k+': '+n+' objects still require an explicit deep validation/result');
if(summary.exportFailures.length)summary.blockers.push('Failed selected export attempts: '+summary.exportFailures.length);
const graphFiles=['dependency-graph.json','additional-dependency-graph.json','probes/additional-cross-db-export-graph.json'].map(f=>path.join(base,f)).filter(f=>fs.existsSync(f));if(graphFiles.length){
 const graphs=graphFiles.map(file=>({file,graph:JSON.parse(fs.readFileSync(file))}));
 const graphSources=new Set(),selected=new Map(),staleGraphSources=new Set();
 summary.referenceGraphs=graphs.map(({file,graph})=>{
  for(const source of graph.sources??[]){graphSources.add(source.path);if(reports.get(source.path)?.output!==source.evidence)staleGraphSources.add(source.path);}
  for(const node of graph.nodes){const key=node.sourcePath+'|'+node.asset_id;if(!selected.has(key))selected.set(key,node);}
  return{file,indexedSources:graph.indexedSources,selectedObjects:graph.nodes.length,generated:graph.generated,counts:graph.counts};
 });
 summary.graphSources=graphSources.size;summary.graphUnresolved=graphs[0].graph.counts;
 if(graphSources.size!==inventory.files.length)summary.blockers.push('Reference graph does not cover the complete corpus');
 summary.staleGraphSources=[...staleGraphSources];
 if(summary.staleGraphSources.length)summary.blockers.push('Reference graph uses stale per-DB evidence');
 // Reconcile the selection itself, not just rows the exporter happened to
 // attempt. A newly discovered dependency otherwise disappears from failure
 // counts until someone manually requests its export.
 const attempted=new Set(),semantic=new Set();
 for(const r of exportResults.values()){
  const key=r.source+'|'+r.id;attempted.add(key);
  if(r.ok&&!r.rawFallback&&r.format!=='raw'&&fs.existsSync(r.path??''))semantic.add(key);
 }
 const describe=n=>({source:n.sourcePath,id:n.asset_id,pathId:n.path_id,type:n.type,name:n.name,reasons:n.reasons});
 const nodes=[...selected.values()];summary.selectedObjects=nodes.length;
 summary.selectedNeverAttempted=nodes.filter(n=>!attempted.has(n.sourcePath+'|'+n.asset_id)).map(describe);
 summary.selectedWithoutSemanticExport=nodes.filter(n=>!semantic.has(n.sourcePath+'|'+n.asset_id)).map(describe);
 if(summary.selectedNeverAttempted.length)summary.blockers.push('Selected objects with no export attempt: '+summary.selectedNeverAttempted.length);
 if(summary.selectedWithoutSemanticExport.length)summary.blockers.push('Selected objects without a successful non-raw export: '+summary.selectedWithoutSemanticExport.length);
}else summary.blockers.push('No dependency graph');
const audioFile=path.join(base,'audio-event-bank-links.json');
if(fs.existsSync(audioFile)){
 const audio=JSON.parse(fs.readFileSync(audioFile));summary.audioEventCalls=audio.declaredCalls;summary.unresolvedAudioEventCalls=audio.unresolvedCalls;
 summary.audioBankIndexFailures=audio.failures.length;
 if(audio.unresolvedCalls)summary.blockers.push('Unresolved declared audio-event calls: '+audio.unresolvedCalls);
 if(audio.failures.length)summary.blockers.push('Audio-bank identity parsing failures: '+audio.failures.length);
}
summary.blockers.push('Full runtime identity closure and independent reimport validation are not yet attested');
fs.writeFileSync(path.join(base,'acceptance-status.json'),JSON.stringify(summary,null,2));console.log(JSON.stringify({planned:summary.planned,completedReports:summary.completedReports,objects:summary.objects,statuses:summary.statuses,successfulExports:summary.successfulExports,verifiedCopiedFiles:summary.verifiedCopiedFiles,deliverable:summary.deliverable,blockers:summary.blockers},null,2));
