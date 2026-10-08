// Exactly one DB per invocation. Other corpus files may be indexed (not loaded)
// only when --dependencies is explicitly requested for streaming resolution.
const fs=require('node:fs'),path=require('node:path'),cp=require('node:child_process'),rl=require('node:readline'),crypto=require('node:crypto');
const root=path.resolve(__dirname,'..'),[buildArg,sourceArg,outArg,typesArg='']=process.argv.slice(2),build=path.resolve(buildArg),source=path.resolve(sourceArg),out=path.resolve(outArg);
const corpus=fs.readFileSync(path.join(root,'tests/hok_db_corpus.txt'),'utf8').trim().split(/\r?\n/);
if(!corpus.some(d=>path.resolve(d).toLowerCase()===path.dirname(source).toLowerCase())||!source.toLowerCase().endsWith('.db'))throw Error('Select one DB from the fixed corpus');
fs.mkdirSync(out,{recursive:true});const hash=f=>new Promise((ok,no)=>{const h=crypto.createHash('sha256'),s=fs.createReadStream(f);s.on('data',x=>h.update(x));s.on('error',no);s.on('end',()=>ok(h.digest('hex')))});
const exe=path.join(build,'worker/Hok.Worker.exe'),worker=cp.spawn(exe,[path.join(out,'preview')],{cwd:path.dirname(exe),windowsHide:true});worker.stderr.pipe(fs.createWriteStream(path.join(out,'worker.log')));
let seq=0;const pending=new Map();rl.createInterface({input:worker.stdout}).on('line',s=>{try{const r=JSON.parse(s),p=pending.get(r.id);if(p){clearTimeout(p.timer);pending.delete(r.id);r.ok?p.ok(r.data):p.no(Error(r.error.message))}}catch{}});
worker.on('exit',c=>{for(const p of pending.values()){clearTimeout(p.timer);p.no(Error('Worker exit '+c))}pending.clear()});
const rpc=(method,payload)=>new Promise((ok,no)=>{const id=String(++seq),timer=setTimeout(()=>{worker.kill();no(Error('Timed out: '+method))},600000);pending.set(id,{ok,no,timer});worker.stdin.write(JSON.stringify({id,method,payload})+'\n')});
function validate(file,format){const b=fs.readFileSync(file);if(!b.length)throw Error('Empty export');if(format==='json'){const obj=JSON.parse(b);if(Object.keys(obj).length===0||Object.keys(obj).every(k=>k==='Name'))throw Error('Only base object header exported');if(obj.complete===false||obj.structureComplete===false||obj.parseStatus==='typed-partial')throw Error('Payload is explicitly partial: '+(obj.unparsedBytes??obj.remainingBytes??'unconfirmed runtime semantics'));return{bytes:b.length}}const text=['anim','obj','xml'].includes(format)?b.toString('utf8'):'';
 if(format==='anim'){if(!text.includes('AnimationClip:')||/\b(?:NaN|Infinity)\b|\.nan/.test(text)||/^\s*(?:time|value):.*\.inf/m.test(text))throw Error('Invalid animation YAML');return{bytes:b.length,curveKeys:(text.match(/\btime:/g)||[]).length};}
 if(format==='obj'){const vertices=(text.match(/^v /gm)||[]).length,faces=(text.match(/^f /gm)||[]).length;if(!vertices||!faces||/\b(?:NaN|Infinity)\b/.test(text))throw Error('Invalid OBJ geometry');return{bytes:b.length,vertices,faces}}
 if(format==='png'&&!b.subarray(0,8).equals(Buffer.from([137,80,78,71,13,10,26,10])))throw Error('Invalid PNG');
 return{bytes:b.length};
}
(async()=>{const report={source,started:new Date().toISOString(),sha256:await hash(source),exports:[],failures:[]};try{
 const dependencyPaths=process.argv.includes('--dependencies')?corpus.flatMap(d=>fs.readdirSync(d).filter(n=>/\.db$/i.test(n)).map(n=>path.join(d,n))):[];
 report.load=await rpc('load',{paths:[source],dependencyPaths});console.log('Loaded',report.load.count,'errors',report.load.errorCount);const types=typesArg.split(',').filter(Boolean),rows=[];
 for(let page=0;;page++){const r=await rpc('list',{page});rows.push(...r.items);if(rows.length>=r.total)break;}
 for(const row of rows.filter(r=>!types.length||types.includes(r.type))){const formats=['HokObjectTree','HokActionTimeline','XmlAsset','TextFile'].includes(row.type)?['json']:row.formats.filter(f=>f!=='raw');let format=formats.find(f=>['anim','obj','png','wav','json','original'].includes(f));if(!format){report.failures.push({id:row.id,type:row.type,error:'No semantic exporter'});continue;}
  if(row.parseStatus==='parser-failed'||row.parseStatus==='generic-raw'){report.failures.push({id:row.id,type:row.type,error:'Semantic parser unavailable',warning:row.warning});continue;}
  try{const r=await rpc('export',{assetIds:[row.id],format,output:path.join(out,'exports')}),item=r.results[0];if(!item.ok)throw Error(item.error);const verified=validate(item.path,format),current=await rpc('asset',{assetId:row.id});report.exports.push({id:row.id,name:row.name,type:row.type,format,parseStatus:current.parseStatus,...verified});}catch(e){report.failures.push({id:row.id,name:row.name,type:row.type,format,error:e.message});}
 }
 }catch(e){report.fatal=e.stack}finally{worker.stdin.end();worker.kill();for(const p of pending.values())clearTimeout(p.timer);report.originalUnchanged=report.sha256===await hash(source);report.finished=new Date().toISOString();fs.writeFileSync(path.join(out,'report.json'),JSON.stringify(report,null,2));console.log('Semantic exports',report.exports.length,'failures',report.failures.length,report.fatal||'');if(report.failures.length||report.fatal||!report.originalUnchanged)process.exitCode=1;}})().catch(e=>{worker.kill();console.error(e);process.exitCode=1});
