// Fixed corpus only. The baseline runs the immutable, previously packaged worker.
const fs=require('node:fs'),path=require('node:path'),cp=require('node:child_process'),rl=require('node:readline'),crypto=require('node:crypto');
const root=path.resolve(__dirname,'..'),build=path.resolve(root,process.argv[2]),out=path.resolve(root,process.argv[3]),corpus=path.join(root,'tests/hok_db_corpus.txt');
const dirs=fs.readFileSync(corpus,'utf8').split(/\r?\n/).filter(x=>x&&!x.startsWith('#'));
if(dirs.length!==10)throw Error('Exactly 10 fixed directories required');fs.mkdirSync(out,{recursive:true});
const shaFile=f=>new Promise((resolve,reject)=>{const h=crypto.createHash('sha256'),s=fs.createReadStream(f);s.on('error',reject);s.on('data',b=>h.update(b));s.on('end',()=>resolve(h.digest('hex')))});
async function check(dir){
 const single=fs.statSync(dir).isFile(),name=single?path.basename(path.dirname(dir))+'__'+path.parse(dir).name:path.basename(dir),reportFile=path.join(out,name+'.json');if(fs.existsSync(reportFile)){console.log('Existing report retained: '+name);return JSON.parse(fs.readFileSync(reportFile));}
 const work=path.join(out,name);fs.mkdirSync(work,{recursive:true});const files=single?[dir]:fs.readdirSync(dir).filter(x=>/\.db$/i.test(x)).sort().map(x=>path.join(dir,x));
 const report={directory:dir,files:[],started:new Date().toISOString(),exportMode:'raw (all exposed objects and containers; overlapping bytes counted as exports, NOT recovery coverage)',exports:{files:0,bytes:0,failed:[]}};
 for(const f of files)report.files.push({path:f,size:fs.statSync(f).size,sha256:await shaFile(f)});
 const worker=cp.spawn(path.join(build,'worker/Hok.Worker.exe'),[path.join(work,'preview')],{cwd:path.join(build,'worker'),windowsHide:true});worker.stderr.pipe(fs.createWriteStream(path.join(work,'worker.log')));
 const budget=process.argv.includes('--desktop-limits')?setTimeout(()=>{report.budgetExceeded='180 seconds for this single DB test (load/list/raw export); completed exports below are partial';worker.kill()},180000):null;
 let seq=0;const pending=new Map();rl.createInterface({input:worker.stdout}).on('line',line=>{try{const r=JSON.parse(line),p=pending.get(r.id);if(p){pending.delete(r.id);clearTimeout(p.timer);r.ok?p.resolve(r.data):p.reject(Error(r.error.message))}}catch(e){console.error(line.slice(0,200))}});
 worker.on('exit',code=>{for(const p of pending.values()){clearTimeout(p.timer);p.reject(Error('Worker exited '+code));}pending.clear()});
 const timeout=process.argv.includes('--desktop-limits')?180000:1200000;
 const rpc=(method,payload)=>new Promise((resolve,reject)=>{const id=String(++seq),timer=setTimeout(()=>{pending.delete(id);reject(Error('Timeout '+method+' after '+timeout+'ms'));worker.kill()},timeout);pending.set(id,{resolve,reject,timer});worker.stdin.write(JSON.stringify({id,method,payload})+'\n')});
 try{
  report.load=await rpc('load',{paths:files});console.log(name+' loaded '+report.load.count+' rows; errors '+report.load.errorCount);
  if(process.argv.includes('--raw-audit')){report.rawAudit=await rpc('rawAudit',{output:path.join(out,'db'),failedChunks:path.join(out,'failed_chunks')});console.log(name+' audited '+JSON.stringify(report.rawAudit));}
  if(process.argv.includes('--verify-raw-list')){
   const seen=new Set();let complete=0,declared=0,recovered=0,total=1;
   for(let page=0;seen.size<total;page++){
    const r=await rpc('rawList',{page});total=r.total;if(!r.items.length&&seen.size<total)throw Error('Raw list page ended before its declared total');
    for(const item of r.items){if(seen.has(item.id))throw Error('Duplicate visible raw identity: '+item.id);seen.add(item.id);if(item.complete)complete++;declared+=item.uncompressedSize;recovered+=item.recoveredSize;}
   }
   if(seen.size!==report.load.rawEntries)throw Error('Raw index and paginated list disagree');
   if(report.rawAudit&&seen.size!==report.rawAudit.reduce((n,a)=>n+a.entryCount,0))throw Error('Raw list does not expose all audited entries');
   report.rawListing={entries:seen.size,complete,declaredUncompressedBytes:declared,recoveredBytes:recovered,evidence:'Every worker rawList page enumerated; browser rendering is a separate test'};
  }
  let total=process.argv.includes('--load-only')?0:1;const rows=[];for(let page=0;rows.length<total;page++){const r=await rpc('list',{page});total=r.total;rows.push(...r.items)}
  fs.writeFileSync(path.join(work,'rows.json'),JSON.stringify(rows));
  if(process.argv.includes('--no-export'))report.exportMode='Not tested in this parser-only diagnostic';
  for(let i=0;!process.argv.includes('--no-export')&&i<rows.length;i+=150){const selected=rows.slice(i,i+150),raw=selected.filter(x=>x.formats.includes('raw'));for(const r of selected.filter(x=>!x.formats.includes('raw')))report.exports.failed.push({id:r.id,error:'Raw export not offered',formats:r.formats});if(!raw.length)continue;
   const r=await rpc('export',{assetIds:raw.map(x=>x.id),format:'raw',output:path.join(work,'exports')});for(const x of r.results){if(x.ok){report.exports.files++;report.exports.bytes+=fs.statSync(x.path).size}else report.exports.failed.push(x)}
  }
 }catch(e){report.failure=e.stack}finally{if(budget)clearTimeout(budget);worker.stdin.end();worker.kill();for(const p of pending.values())clearTimeout(p.timer)}
 report.originalsUnchanged=true;for(const f of report.files)if(f.sha256!==await shaFile(f.path))report.originalsUnchanged=false;
 report.finished=new Date().toISOString();fs.writeFileSync(reportFile,JSON.stringify(report,null,2));console.log(name+' exported '+report.exports.files+' / '+report.exports.bytes+' bytes');return report;
}
(async()=>{const onlyAt=process.argv.indexOf('--only'),only=onlyAt<0?null:path.resolve(process.argv[onlyAt+1]);if(only&&!dirs.some(d=>path.dirname(only).toLowerCase()===path.resolve(d).toLowerCase())||only&&!/\.db$/i.test(only))throw Error('The selected DB must belong to the fixed corpus');const result=[];for(const d of dirs){const inputs=only?(path.dirname(only).toLowerCase()===path.resolve(d).toLowerCase()?[only]:[]):process.argv.includes('--per-db')?fs.readdirSync(d).filter(x=>/\.db$/i.test(x)).sort().map(x=>path.join(d,x)):[d];for(const input of inputs)result.push(await check(input));}if(!only)fs.writeFileSync(path.join(out,'summary.json'),JSON.stringify(result,null,2));console.log((only?'SINGLE DB COMPLETE ':'CORPUS COMPLETE ')+out);if(result.some(x=>x.failure||!x.originalsUnchanged))process.exitCode=1})().catch(e=>{console.error(e);process.exitCode=1});
