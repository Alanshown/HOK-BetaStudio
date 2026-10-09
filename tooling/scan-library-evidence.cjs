// Sequential, resumable: exactly one DB is loaded in one worker at a time.
const fs=require('node:fs'),fsp=fs.promises,path=require('node:path'),cp=require('node:child_process'),rl=require('node:readline'),crypto=require('node:crypto');
const {scanStatus}=require('./evidence-scan-status.cjs');
const [buildArg,inputArg,outputArg,query='14006',limitArg,schemaArg]=process.argv.slice(2);
if(!outputArg)throw Error('Usage: node scan-library-evidence.cjs <build> <all> <output> [query] [limit]');
const build=path.resolve(buildArg),input=path.resolve(inputArg),output=path.resolve(outputArg),evidence=path.join(output,'evidence'),cacheRoot=path.resolve(process.env.HOK_EVIDENCE_CACHE_ROOT||path.join(output,'.work'));
const deep=process.env.HOK_EVIDENCE_DEEP==='1';
const typeSchemaPaths=schemaArg?[path.resolve(schemaArg)]:[],schemaRevision=crypto.createHash('sha256').update('nested-qts-typetree-v2').update([...typeSchemaPaths,path.join(build,'worker/Hok.Worker.dll'),path.join(build,'worker/Hok.Legacy.dll')].map(f=>crypto.createHash('sha256').update(fs.readFileSync(f)).digest('hex')).join('|')).update(deep?'|deep-object-validation-v1':'').digest('hex');
if(output===input||output.startsWith(input+path.sep))throw Error('Output must be outside input');
if(cacheRoot===input||cacheRoot.startsWith(input+path.sep))throw Error('Temporary cache must be outside input');
function files(dir){const list=[];for(const e of fs.readdirSync(dir,{withFileTypes:true})){if(e.isSymbolicLink())continue;const p=path.join(dir,e.name);if(e.isDirectory())list.push(...files(p));else if(e.isFile()&&/\.db$/i.test(e.name))list.push(p);}return list;}
function atomic(file,value){const temp=file+'.tmp';fs.writeFileSync(temp,JSON.stringify(value,null,2));fs.renameSync(temp,file);}
async function scan(source,index){
 const key=crypto.createHash('sha256').update(source.toLowerCase()).digest('hex').slice(0,24),dest=path.join(evidence,'db',key+'.jsonl'),reportFile=path.join(evidence,'db',key+'.report.json'),stat=fs.statSync(source);
 if(fs.existsSync(reportFile)){const previous=JSON.parse(fs.readFileSync(reportFile));if(previous.schemaRevision===schemaRevision&&previous.ok&&!(previous.load?.errorCount>0)&&!(previous.indexed?.errors>0)&&!(previous.indexed?.entryIssues>0)&&fs.existsSync(previous.output)&&previous.bytes===stat.size&&previous.mtimeMs===stat.mtimeMs&&previous.query===query)return{...previous,resumed:true};}
 // A prior incomplete output is kept separately rather than silently overwritten.
 const suffix=fs.existsSync(dest)||fs.existsSync(dest+'.partial')?'-'+Date.now():'',destination=suffix?dest.replace('.jsonl',suffix+'.jsonl'):dest;
 const cache=path.join(cacheRoot,crypto.randomUUID()),logFile=path.join(evidence,'logs',key+'.log');fs.mkdirSync(cache,{recursive:true});
 const worker=cp.spawn(path.join(build,'worker/Hok.Worker.exe'),[cache],{cwd:path.join(build,'worker'),windowsHide:true});
 const exited=new Promise(resolve=>worker.once('exit',resolve)),log=fs.createWriteStream(logFile);worker.stderr.pipe(log);
 let serial=0;const pending=new Map();rl.createInterface({input:worker.stdout}).on('line',s=>{try{const r=JSON.parse(s),p=pending.get(r.id);if(p){clearTimeout(p.timer);pending.delete(r.id);r.ok?p.resolve(r.data):p.reject(Error(r.error.message));}}catch(e){log.write('RPC: '+e.message+'\n');}});
 worker.on('exit',code=>{for(const p of pending.values()){clearTimeout(p.timer);p.reject(Error('Worker exited '+code));}pending.clear();});
 const rpc=(method,payload)=>new Promise((resolve,reject)=>{const id=String(++serial),timer=setTimeout(()=>{reject(Error('Timeout '+method));worker.kill();},20*60*1000);pending.set(id,{resolve,reject,timer});worker.stdin.write(JSON.stringify({id,method,payload})+'\n');});
 const report={index,source,bytes:stat.size,mtimeMs:stat.mtimeMs,query,deep,schemaRevision,started:new Date().toISOString(),output:destination,log:logFile,ok:false};
 // Deep auditing is explicit and isolated to this offline worker. It must
 // not enable eager preview decoding in the desktop application, and a
 // shallow report cannot satisfy this cache identity.
 try{report.load=await rpc('load',{paths:[source],typeSchemaPaths});report.indexed=await rpc('evidenceIndex',{output:destination,query,deep});report.ok=true;report.readErrors=report.load.errorCount+report.indexed.errors+report.indexed.entryIssues;report.semanticComplete=false;}
 catch(e){report.error=e.stack;}
 finally{worker.stdin.end();let stopTimer;try{await Promise.race([exited,new Promise(resolve=>{stopTimer=setTimeout(()=>{worker.kill();resolve();},3000);})]);}finally{clearTimeout(stopTimer);}for(const p of pending.values())clearTimeout(p.timer);log.end();report.finished=new Date().toISOString();atomic(reportFile,report);
  const absolute=path.resolve(cache);if(!absolute.startsWith(cacheRoot+path.sep)||!/^[-0-9a-f]{36}$/i.test(path.basename(absolute)))throw Error('Unsafe cache cleanup');await fsp.rm(absolute,{recursive:true,force:true,maxRetries:5,retryDelay:100});}
 return report;
}
(async()=>{
 for(const d of [path.join(evidence,'db'),path.join(evidence,'logs'),cacheRoot])fs.mkdirSync(d,{recursive:true});
 let list=files(input).sort((a,b)=>Number(!a.includes(query))-Number(!b.includes(query))||a.localeCompare(b,'en',{numeric:true}));
 // Fill never-scanned coverage first on a resumed investigation, then still
 // revalidate every stale report. This changes order, never the inventory.
 if(process.env.HOK_EVIDENCE_ORDER==='unscanned-first'){
  const known=new Set(fs.readdirSync(path.join(evidence,'db')).filter(f=>f.endsWith('.report.json')));
  const existing=new Map(list.map(source=>[source,known.has(crypto.createHash('sha256').update(source.toLowerCase()).digest('hex').slice(0,24)+'.report.json')]));
  list.sort((a,b)=>Number(existing.get(a))-Number(existing.get(b)));
 }
 const inventory=list,total=inventory.length;
 if(process.env.HOK_EVIDENCE_ONLY_SOURCE){
  const target=process.env.HOK_EVIDENCE_ONLY_SOURCE;
  list=inventory.filter(source=>path.isAbsolute(target)?source.toLowerCase()===path.resolve(target).toLowerCase():path.basename(source).toLowerCase()===target.toLowerCase());
  if(list.length!==1)throw Error('Single-source scan requires exactly one inventory match: '+target);
 }
 if(limitArg&&limitArg!=='all')list=list.slice(0,Number(limitArg));
 // A targeted regression must never erase unvisited files from the corpus
 // inventory and thereby turn a subset into a false completeness claim.
 atomic(path.join(evidence,'inventory.json'),{input,output,query,total,bytes:inventory.reduce((n,f)=>n+fs.statSync(f).size,0),files:inventory,plannedThisRun:list.length});
 const summary={schema:3,input,output,query,total,planned:list.length,completed:0,failed:0,readErrors:0,incompleteObjects:0,objects:0,references:0,hits:0,results:[]};
 for(let i=0;i<list.length;i++){if(fs.existsSync(path.join(evidence,'PAUSE'))){console.log(JSON.stringify({paused:true,next:list[i]}));break;}const result=await scan(list[i],i+1),status=scanStatus(result);summary.completed++;if(!status.ok)summary.failed++;summary.readErrors+=status.readErrors;summary.incompleteObjects+=status.incompleteObjects;summary.objects+=result.indexed?.objects??0;summary.references+=result.indexed?.references??0;summary.hits+=result.indexed?.hits??0;
  summary.results.push({source:result.source,...status,output:result.output,hits:result.indexed?.hits,objects:result.indexed?.objects,error:result.error,resumed:result.resumed});
  atomic(path.join(evidence,'progress.json'),summary);console.log(JSON.stringify({progress:i+1,total,db:path.relative(input,list[i]),...status,objects:result.indexed?.objects,hits:result.indexed?.hits,error:result.error}));
 }console.log(JSON.stringify({done:true,completed:summary.completed,failed:summary.failed,readErrors:summary.readErrors,incompleteObjects:summary.incompleteObjects,objects:summary.objects,references:summary.references,hits:summary.hits}));
})().catch(e=>{console.error(e);process.exitCode=1});
