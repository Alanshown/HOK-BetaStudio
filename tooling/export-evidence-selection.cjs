// Exports explicit graph selections, one DB/worker at a time. Failed semantic
// conversions remain failures; this tool never labels a raw fallback successful.
const fs=require('node:fs'),path=require('node:path'),cp=require('node:child_process'),rl=require('node:readline'),crypto=require('node:crypto');
const exportCache=require('./evidence-export-cache.cjs');
const [buildArg,rootArg,onlySource,schemaPath,onlyType,onlyAssetId]=process.argv.slice(2),build=path.resolve(buildArg),root=path.resolve(rootArg),base=path.join(root,'evidence'),assetRoot=path.join(root,'asset');
const onlyAssetIds=onlyAssetId?new Set(onlyAssetId.split('|')):null;
const parserRevision=crypto.createHash('sha256').update(fs.readFileSync(path.join(build,'worker/Hok.Legacy.dll'))).update(fs.readFileSync(path.join(build,'worker/Hok.Worker.dll'))).digest('hex');
const graph=JSON.parse(fs.readFileSync(process.env.HOK_EVIDENCE_GRAPH?path.resolve(process.env.HOK_EVIDENCE_GRAPH):path.join(base,'dependency-graph.json'))),inventory=JSON.parse(fs.readFileSync(path.join(base,'inventory.json'))),manifest=path.join(base,'asset-exports.jsonl');
const dependencies=inventory.files.map(file=>{const stat=fs.statSync(file);return{path:path.resolve(file).toLowerCase(),bytes:stat.size,mtimeMs:stat.mtimeMs};});
const schemaSha256=schemaPath?crypto.createHash('sha256').update(fs.readFileSync(schemaPath)).digest('hex'):null;
const groups=new Map();for(const node of graph.nodes){if(onlySource&&(/\.db$/i.test(onlySource)?path.basename(node.sourcePath).toLowerCase()!==onlySource.toLowerCase():!node.sourcePath.includes(onlySource)))continue;if(!groups.has(node.sourcePath))groups.set(node.sourcePath,[]);groups.get(node.sourcePath).push(node);}
async function sha(file){const h=crypto.createHash('sha256');for await(const b of fs.createReadStream(file))h.update(b);return h.digest('hex');}
function formats(row){const desired=row.type==='Mesh'?['obj','fbx']:row.type==='AnimationClip'?['anim','curves-json','fbx']:row.type==='Cubemap'?['zip-png','png']:row.preview==='image'?['png']:row.preview==='bank'?['original','zip-wem','zip-mp3']:row.preview==='audio'?['original','mp3','wav']:row.formats.includes('json')?['json']:['original'];return desired.filter(f=>row.formats.includes(f));}
const prior=new Map();if(fs.existsSync(manifest))for(const line of fs.readFileSync(manifest,'utf8').split(/\r?\n/).filter(Boolean)){const row=JSON.parse(line);if(row.ok)prior.set(row.source+'|'+row.id+'|'+row.format,row);}
async function run(source,nodes){
 const sourceSha256=await sha(source),sourceEvidence=graph.sources.find(s=>s.path===source);
 if(!sourceEvidence?.sha256||sourceSha256!==sourceEvidence.sha256.toLowerCase())throw Error('Source DB differs from its graph evidence; rescan before export: '+source);
 const scoped=process.env.HOK_EVIDENCE_SCOPED_EXPORT==='1'&&path.basename(path.dirname(source))!=='3200014006';
 const selectedEntries=[...new Set(nodes.map(n=>n.entry))];
 const exportContext=exportCache.context({parserRevision,sourceSha256,schemaSha256,dependencies,selectedEntries:scoped?selectedEntries:null});
 const key=crypto.createHash('sha256').update(source.toLowerCase()).digest('hex').slice(0,24),cacheRoot=path.resolve(process.env.HOK_EVIDENCE_CACHE_ROOT||path.join(root,'.work')),cache=path.join(cacheRoot,crypto.randomUUID());fs.mkdirSync(cache,{recursive:true});
 const child=cp.spawn(path.join(build,'worker/Hok.Worker.exe'),[cache],{cwd:path.join(build,'worker'),windowsHide:true}),log=fs.createWriteStream(path.join(base,'logs','export-'+key+'.log'));child.stderr.pipe(log);
 const pending=new Map();let serial=0;const exited=new Promise(resolve=>child.once('exit',resolve));
 rl.createInterface({input:child.stdout}).on('line',line=>{const r=JSON.parse(line),p=pending.get(r.id);if(p){clearTimeout(p.timer);pending.delete(r.id);r.ok?p.resolve(r.data):p.reject(Error(r.error.message));}});
 child.on('exit',code=>{for(const p of pending.values()){clearTimeout(p.timer);p.reject(Error('Worker exited '+code));}pending.clear();});
 const rpc=(method,payload)=>new Promise((resolve,reject)=>{const id=String(++serial),timer=setTimeout(()=>{pending.delete(id);reject(Error('Timeout '+method));child.kill();},20*60*1000);pending.set(id,{resolve,reject,timer});child.stdin.write(JSON.stringify({id,method,payload})+'\n');});
 let success=0,failed=0,skipped=0;
 try{
  const load=await rpc('load',{paths:[source],dependencyPaths:inventory.files,typeSchemaPaths:schemaPath?[path.resolve(schemaPath)]:[],...(scoped?{qtsEntrySelection:{[source]:selectedEntries}}:{})});fs.writeFileSync(path.join(base,'export-load-'+key+'.json'),JSON.stringify(load,null,2));
  if(load.errorCount>0)throw Error('Selected source has '+load.errorCount+' load errors; inspect its log before exporting');
  const rows=new Map();for(const node of nodes)rows.set(node.asset_id,await rpc('asset',{assetId:node.asset_id}));
  if(path.basename(path.dirname(source))==='3200014006')for(let page=0;;page++){const result=await rpc('list',{page});for(const row of result.items)rows.set(row.id,row);if((page+1)*200>=result.total)break;}
  if(onlyAssetIds)for(const id of onlyAssetIds)if(!rows.has(id))throw Error('Requested asset is not in this source selection: '+id);
  const jobs=new Map();for(const row of [...rows.values()].filter(r=>(!onlyType||r.type===onlyType)&&(!onlyAssetIds||onlyAssetIds.has(r.id))))for(const format of formats(row)){
   const previous=prior.get(source+'|'+row.id+'|'+format);
   if(exportCache.reusable(previous,exportContext)&&fs.existsSync(previous.path)&&await sha(previous.path)===previous.sha256){skipped++;continue;}
   const jobKey=row.type+'|'+format;if(!jobs.has(jobKey))jobs.set(jobKey,[]);jobs.get(jobKey).push(row);
  }
  for(const [key,rows]of jobs){const[type,format]=key.split('|'),output=path.join(assetRoot,path.basename(path.dirname(source)),path.basename(source,'.db'),type,format);
   for(let at=0;at<rows.length;at+=20){const batch=rows.slice(at,at+20),result=await rpc('export',{assetIds:batch.map(r=>r.id),format,output});
    for(const item of result.results){const row=batch.find(r=>r.id===item.id),node=nodes.find(n=>n.asset_id===item.id);const saved={source,sourceSha256,parserRevision,exportContext,...item,name:row.name,type:row.type,pathId:row.pathId,evidence:node?.reasons??['seed-package-container'],semanticCompletenessNotClaimed:true};
     if(item.ok){saved.sha256=await sha(item.path);saved.bytes=fs.statSync(item.path).size;if(!saved.bytes||item.rawFallback)throw Error('Unexpected empty/raw fallback export');success++;prior.set(source+'|'+item.id+'|'+format,saved);}else failed++;
     fs.appendFileSync(manifest,JSON.stringify(saved)+'\n');
    }
    console.log(JSON.stringify({source,type,format,done:Math.min(at+20,rows.length),total:rows.length,success,failed}));
   }
  }
  console.log(JSON.stringify({source,selected:rows.size,success,failed,skipped}));
 }finally{child.stdin.end();await Promise.race([exited,new Promise(resolve=>setTimeout(()=>{child.kill();resolve();},3000))]);log.end();
  const absolute=path.resolve(cache);if(!absolute.startsWith(cacheRoot+path.sep)||!/^[-0-9a-f]{36}$/i.test(path.basename(absolute)))throw Error('Unsafe cache path');await fs.promises.rm(absolute,{recursive:true,force:true,maxRetries:5,retryDelay:100});}
}
(async()=>{fs.mkdirSync(assetRoot,{recursive:true});for(const[source,nodes]of groups)await run(source,nodes);})().catch(e=>{console.error(e);process.exitCode=1});
