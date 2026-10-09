const fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto'),cp=require('node:child_process'),assert=require('node:assert/strict');
const scratch=fs.mkdtempSync(path.resolve('.cache/evidence-acceptance-')),root=path.join(scratch,'output'),base=path.join(root,'evidence');
fs.mkdirSync(path.join(base,'db'),{recursive:true});
const source=path.join(scratch,'fixture.db');fs.writeFileSync(source,Buffer.from([1]));const stat=fs.statSync(source),evidence=path.join(base,'fixture.jsonl');fs.writeFileSync(evidence,'{}\n');
function json(file,value){fs.writeFileSync(file,JSON.stringify(value));}
json(path.join(base,'inventory.json'),{query:'14006',files:[source]});
const key=crypto.createHash('sha256').update(source.toLowerCase()).digest('hex').slice(0,24);
json(path.join(base,'db',key+'.report.json'),{source,ok:true,output:evidence,bytes:stat.size,mtimeMs:stat.mtimeMs,schemaRevision:'fixture',indexed:{objects:2,statuses:{'typed-complete':2}},load:{errorCount:0}});
json(path.join(base,'dependency-graph.json'),{indexedSources:1,sources:[{path:source,evidence}],counts:{},nodes:['first','new-dependency'].map(asset_id=>({sourcePath:source,asset_id,type:'Material',name:asset_id,reasons:['fixture']}))});
const first=path.join(root,'first.json'),second=path.join(root,'second.json');json(first,{});json(second,{});
const rows=[{source,id:'first',format:'json',ok:true,path:first}];
function audit(){fs.writeFileSync(path.join(base,'asset-exports.jsonl'),rows.map(r=>JSON.stringify(r)).join('\n')+'\n');const run=cp.spawnSync(process.execPath,[path.join(__dirname,'audit-evidence-completeness.cjs'),root],{encoding:'utf8',windowsHide:true});assert.equal(run.status,0,run.stderr);return JSON.parse(fs.readFileSync(path.join(base,'acceptance-status.json')));}
let r=audit();assert.equal(r.selectedNeverAttempted.length,1);assert.equal(r.selectedWithoutSemanticExport.length,1);assert(r.blockers.some(x=>x.includes('no export attempt')));
rows.push({source,id:'new-dependency',format:'json',ok:false,error:'fixture unavailable'});r=audit();assert.equal(r.selectedNeverAttempted.length,0);assert.equal(r.selectedWithoutSemanticExport.length,1);assert.equal(r.exportFailures.length,1);
rows.push({source,id:'new-dependency',format:'raw',ok:true,path:second,rawFallback:true});r=audit();assert.equal(r.selectedWithoutSemanticExport.length,1);
rows.push({source,id:'new-dependency',format:'json',ok:true,path:second});r=audit();assert.equal(r.selectedWithoutSemanticExport.length,0);assert.equal(r.selectedNeverAttempted.length,0);assert.equal(r.deliverable,false,'File coverage alone cannot claim runtime dependency closure');
fs.unlinkSync(second);r=audit();assert.equal(r.selectedWithoutSemanticExport.length,1);assert(r.exportFailures.some(x=>x.format==='json'&&x.id==='new-dependency'));
json(path.join(base,'additional-dependency-graph.json'),{indexedSources:1,sources:[{path:source,evidence}],counts:{missing:1},nodes:['first','additional-script'].map(asset_id=>({sourcePath:source,asset_id,type:'MonoBehaviour',name:asset_id,reasons:['reverse-use-of-named-asset']}))});
r=audit();assert.equal(r.referenceGraphs.length,2);assert.equal(r.selectedObjects,3,'Do not double-count overlapping graph selections');assert.equal(r.graphSources,1,'Do not double-count indexed sources');assert(r.selectedNeverAttempted.some(n=>n.id==='additional-script'),'Supplemental discoveries must affect completeness');
console.log(JSON.stringify({passed:6,scratch,checks:['new graph dependency is not invisible','failed attempt remains incomplete','raw fallback does not satisfy semantic export','successful exports do not imply runtime completeness','missing output file invalidates the saved success','additional graphs reconciled without duplicate objects/sources']}));
