// Real package export coverage. No input modification; failures remain explicit.
const fs=require('node:fs'),path=require('node:path'),cp=require('node:child_process'),readline=require('node:readline'),assert=require('node:assert/strict'),crypto=require('node:crypto');
const root=path.resolve(__dirname,'..'),build=path.resolve(root,process.argv[2]),source=path.resolve(process.argv[3]);
const temp=fs.mkdtempSync(path.join(root,'.cache','asset-exports-'));
const inputs=fs.readdirSync(source).filter(n=>/\.db$/i.test(n)).map(n=>path.join(source,n));
const hash=f=>crypto.createHash('sha256').update(fs.readFileSync(f)).digest('hex'),before=inputs.map(hash);
const worker=cp.spawn(path.join(build,'worker/Hok.Worker.exe'),[path.join(temp,'preview')],{cwd:path.join(build,'worker'),windowsHide:true});
const log=fs.createWriteStream(path.join(temp,'worker.log'));worker.stderr.pipe(log);
const pending=new Map();let serial=0;
readline.createInterface({input:worker.stdout}).on('line',s=>{const r=JSON.parse(s),p=pending.get(r.id);if(p){clearTimeout(p.timer);pending.delete(r.id);r.ok?p.resolve(r.data):p.reject(Error(r.error.message))}});
const rpc=(method,payload)=>new Promise((resolve,reject)=>{const id=String(++serial),timer=setTimeout(()=>{pending.delete(id);reject(Error('Timeout '+method))},180000);pending.set(id,{resolve,reject,timer});worker.stdin.write(JSON.stringify({id,method,payload})+'\n')});
async function main(){try{
 const loaded=await rpc('load',{paths:inputs});console.log(JSON.stringify(loaded));assert(loaded.count>0);
 const all=[];for(let page=0;all.length<loaded.count;page++){const r=await rpc('list',{page});assert(r.items.length);all.push(...r.items)}
 const coverage=[];
 for(const [type,format] of [['*','raw'],['Texture2D','png'],['Sprite','png'],['Mesh','obj'],['AnimationClip','anim'],['WwiseAudio','mp3'],['WwiseBank','zip-wem']]){
  if(type==='*'&&process.argv.includes('--skip-raw'))continue;
  const rows=all.filter(r=>type==='*'||r.type===type);let success=0,failed=0;const errors=[];
  for(let offset=0;offset<rows.length;offset+=200){const batch=rows.slice(offset,offset+200);const report=await rpc('export',{assetIds:batch.map(r=>r.id),format,output:path.join(temp,type==='*'?'raw':type+'-'+format)});success+=report.success;failed+=report.failed;errors.push(...report.results.filter(r=>!r.ok));for(const result of report.results.filter(r=>r.ok)){assert(fs.existsSync(result.path));if(format==='raw'){const row=batch.find(r=>r.id===result.id);assert.equal(fs.statSync(result.path).size,Number(row.bytes))}}}
  coverage.push({type,format,count:rows.length,success,failed,errors});console.log(JSON.stringify(coverage.at(-1)));
 }
 assert.deepEqual(inputs.map(hash),before);
 const report={source,temp,loaded,coverage,originalsUnchanged:true};fs.writeFileSync(path.join(temp,'report.json'),JSON.stringify(report,null,2));console.log('Report: '+path.join(temp,'report.json'));
 assert.equal(coverage[0].failed,0,'Every listed row must remain raw-exportable');
 if(process.argv.includes('--strict'))assert(coverage.every(r=>r.failed===0),'Semantic exports failed; inspect report');
}finally{worker.kill();for(const p of pending.values())clearTimeout(p.timer);log.end()}}
main().catch(e=>{console.error(e);process.exitCode=1});
