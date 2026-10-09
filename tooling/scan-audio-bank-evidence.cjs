// Sequential bank-only discovery across existing full-DB reports. At most one
// game-DB worker is active. This is not a replacement for full object parsing.
const fs=require('node:fs'),path=require('node:path'),cp=require('node:child_process');
const [build,rootArg]=process.argv.slice(2),root=path.resolve(rootArg),base=path.join(root,'evidence');
const reports=fs.readdirSync(path.join(base,'db')).filter(f=>f.endsWith('.report.json')).map(f=>JSON.parse(fs.readFileSync(path.join(base,'db',f)))).filter(r=>(r.load?.types?.WwiseBank??0)+(r.load?.types?.WwisePackage??0)>0).sort((a,b)=>a.source.localeCompare(b.source,'en',{numeric:true}));
function known(){const map=new Map();for(const file of ['asset-exports.jsonl','audio-bank-exports.jsonl'])if(fs.existsSync(path.join(base,file)))for(const line of fs.readFileSync(path.join(base,file),'utf8').split(/\r?\n/).filter(Boolean)){const r=JSON.parse(line);if(r.type==='WwiseBank'&&r.format==='original'){if(!map.has(r.source))map.set(r.source,new Map());map.get(r.source).set(r.id,r);}}return map;}
const saved=known(),results=[];let visited=0;
for(const report of reports){
 const previous=[...(saved.get(report.source)?.values()??[])],expectedBanks=report.load.types.WwiseBank??0;
 const reusable=expectedBanks>0&&previous.length===expectedBanks&&previous.every(r=>r.ok&&r.sourceSha256?.toLowerCase()===report.indexed.sourceSha256.toLowerCase()&&fs.existsSync(r.path));
 if(fs.existsSync(path.join(base,'PAUSE'))){console.log(JSON.stringify({paused:true,next:report.source}));break;}
 if(!reusable){
  const child=cp.spawnSync(process.execPath,[path.join(__dirname,'collect-audio-bank-evidence.cjs'),build,root,path.basename(report.source)],{stdio:'inherit',windowsHide:true});
  if(child.status!==0)throw Error('Bank discovery failed: '+report.source);
  if(++visited%25===0){const match=cp.spawnSync(process.execPath,[path.join(__dirname,'match-exported-audio-events.cjs'),root],{stdio:'inherit',windowsHide:true});if(match.status!==0)throw Error('Bank matching failed');}
 }
 results.push({source:report.source,sourceSha256:report.indexed.sourceSha256,expectedBanks,reused:reusable});
 fs.writeFileSync(path.join(base,'audio-bank-source-coverage.json'),JSON.stringify({planned:reports.length,completed:results.length,scope:'Bank identities recognized by per-DB reports; not complete resource semantics',results},null,2));
 console.log(JSON.stringify({progress:results.length,total:reports.length,source:report.source,expectedBanks,reused:reusable}));
}
const match=cp.spawnSync(process.execPath,[path.join(__dirname,'match-exported-audio-events.cjs'),root],{stdio:'inherit',windowsHide:true});if(match.status!==0)throw Error('Bank matching failed');
