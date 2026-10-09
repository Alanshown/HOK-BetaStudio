// Targeted container-only pass; one game DB worker at a time. Existing full
// source evidence supplies the exact package entries and original source hash.
const fs=require('node:fs'),path=require('node:path'),rl=require('node:readline'),cp=require('node:child_process'),crypto=require('node:crypto');
const [buildArg,rootArg,sourceName,mode]=process.argv.slice(2),build=path.resolve(buildArg),root=path.resolve(rootArg),base=path.join(root,'evidence');
async function hash(file){const h=crypto.createHash('sha256');for await(const chunk of fs.createReadStream(file))h.update(chunk);return h.digest('hex');}
(async()=>{
 const reports=fs.readdirSync(path.join(base,'db')).filter(f=>f.endsWith('.report.json')).map(f=>JSON.parse(fs.readFileSync(path.join(base,'db',f)))).filter(r=>path.basename(r.source).toLowerCase()===sourceName.toLowerCase());
 if(reports.length!==1)throw Error('Expected exactly one source report for '+sourceName);const report=reports[0];
 if(!report.ok||await hash(report.source)!==report.indexed.sourceSha256.toLowerCase())throw Error('Source no longer matches full-scan evidence');
 const entries=[];for await(const line of rl.createInterface({input:fs.createReadStream(report.output),crlfDelay:Infinity}))if(line.startsWith('{"kind":"entry"')){const row=JSON.parse(line);if(['WwiseBank','WwisePackage'].includes(row.payloadKind))entries.push(row.id);}
 let matchedBanks=null,matchEvidence=[];
 if(mode==='matched'){
  const links=JSON.parse(fs.readFileSync(path.join(base,'audio-event-bank-links.json')));
  const events=links.matches.flatMap(m=>m.candidates).filter(c=>c.source===report.source);
  const configured=(links.configuredBankCandidates??[]).flatMap(m=>m.candidates).filter(c=>c.source===report.source);
  matchedBanks=new Set([...events,...configured].map(c=>c.bankAssetId));
  if(events.length)matchEvidence.push('declared-audio-event-hash-to-bank-candidate');
  if(configured.length)matchEvidence.push('configured-bank-name-hash-candidate');
  const selected=new Set([...matchedBanks].map(id=>id.split('/')[1]));for(let i=entries.length-1;i>=0;i--)if(!selected.has(entries[i]))entries.splice(i,1);
 }
 if(!entries.length)throw Error('No Wwise package entries recorded in '+sourceName);
 const cacheRoot=path.resolve(process.env.HOK_EVIDENCE_CACHE_ROOT||path.join(root,'.work')),cache=fs.mkdtempSync(path.join(cacheRoot,'audio-evidence-'));
 const child=cp.spawn(path.join(build,'worker/Hok.Worker.exe'),[cache],{cwd:path.join(build,'worker'),windowsHide:true});
 const exited=new Promise(resolve=>child.once('exit',resolve)),log=fs.createWriteStream(path.join(base,'logs','audio-'+sourceName+'.log'));child.stderr.pipe(log);const pending=new Map();let serial=0;
 rl.createInterface({input:child.stdout}).on('line',line=>{try{const r=JSON.parse(line),p=pending.get(r.id);if(p){clearTimeout(p.timer);pending.delete(r.id);r.ok?p.resolve(r.data):p.reject(Error(r.error.message));}}catch(e){child.kill();for(const p of pending.values())p.reject(e);}});
 child.on('exit',code=>{for(const p of pending.values()){clearTimeout(p.timer);p.reject(Error('Worker exited '+code));}pending.clear();});
 const rpc=(method,payload)=>new Promise((resolve,reject)=>{const id=String(++serial),timer=setTimeout(()=>{pending.delete(id);reject(Error('Timed out '+method));child.kill();},20*60*1000);pending.set(id,{resolve,reject,timer});child.stdin.write(JSON.stringify({id,method,payload})+'\n');});
 try{
  const load=await rpc('load',{paths:[report.source],qtsEntrySelection:{[report.source]:entries}});if(load.errorCount)throw Error(JSON.stringify(load.errors));console.log(JSON.stringify({source:report.source,entries:entries.length,load}));
  const banks=[];for(let page=0;;page++){const list=await rpc('list',{type:'WwiseBank',page});banks.push(...list.items);if((page+1)*200>=list.total)break;}
  const output=mode==='matched'?path.join(root,'asset',path.basename(path.dirname(report.source)),path.basename(report.source,'.db'),'Audio'):path.join(base,'audio-banks',path.basename(path.dirname(report.source)),path.basename(report.source,'.db'));let success=0,failed=0;
  for(let i=0;i<banks.length;i+=20){const result=await rpc('export',{assetIds:banks.slice(i,i+20).map(r=>r.id),format:'original',output});
   for(const item of result.results){const saved={source:report.source,sourceSha256:report.indexed.sourceSha256,...item,type:'WwiseBank',scope:'bank discovery; relevance to requested skin not yet established'};if(item.ok){saved.sha256=await hash(item.path);success++;}else failed++;
    fs.appendFileSync(path.join(base,'audio-bank-exports.jsonl'),JSON.stringify(saved)+'\n');}
  }
  if(mode==='matched'){
   const rows=[...banks.filter(b=>matchedBanks.has(b.id))];
   for(let page=0;;page++){const list=await rpc('list',{type:'WwiseAudio',page});rows.push(...list.items);if((page+1)*200>=list.total)break;}
   for(const type of ['WwiseBank','WwiseAudio'])for(const format of type==='WwiseBank'?['original','zip-wem','zip-mp3']:['original','mp3']){
    const selected=rows.filter(r=>r.type===type);for(let i=0;i<selected.length;i+=20){const result=await rpc('export',{assetIds:selected.slice(i,i+20).map(r=>r.id),format,output:path.join(output,type,format)});
     for(const item of result.results){const row=rows.find(r=>r.id===item.id);const saved={source:report.source,sourceSha256:report.indexed.sourceSha256,...item,type:row.type,name:row.name,pathId:row.pathId,evidence:[...matchEvidence,'complete-enclosing-audio-package'],semanticCompletenessNotClaimed:true};
      if(item.ok){saved.sha256=await hash(item.path);saved.bytes=fs.statSync(item.path).size;success++;}else failed++;
      fs.appendFileSync(path.join(base,'asset-exports.jsonl'),JSON.stringify(saved)+'\n');}
    }
   }
  }
  console.log(JSON.stringify({source:report.source,banks:banks.length,success,failed,mode:mode??'discovery'}));if(failed)process.exitCode=1;
 }finally{
  child.stdin.end();let stopTimer;try{await Promise.race([exited,new Promise(resolve=>{stopTimer=setTimeout(()=>{child.kill();resolve();},3000);})]);}finally{clearTimeout(stopTimer);}log.end();
  const absolute=path.resolve(cache);if(!absolute.startsWith(cacheRoot+path.sep)||!path.basename(absolute).startsWith('audio-evidence-'))throw Error('Unsafe cache cleanup');await fs.promises.rm(absolute,{recursive:true,force:true,maxRetries:5,retryDelay:100});
 }
})().catch(e=>{console.error(e);process.exitCode=1});
