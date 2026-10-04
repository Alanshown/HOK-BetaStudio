// A semantic conversion failure must be explicit; auto mode preserves raw bytes.
const fs=require('node:fs'),path=require('node:path'),cp=require('node:child_process'),rl=require('node:readline'),assert=require('node:assert/strict');
const root=path.resolve(__dirname,'..'),build=path.resolve(root,process.argv[2]),source=path.resolve(process.argv[3]);
const temp=fs.mkdtempSync(path.join(root,'.cache','export-fallback-'));
const worker=cp.spawn(path.join(build,'worker/Hok.Worker.exe'),[path.join(temp,'preview')],{cwd:path.join(build,'worker'),windowsHide:true});
worker.stderr.pipe(fs.createWriteStream(path.join(temp,'worker.log')));
let serial=0;const pending=new Map();
rl.createInterface({input:worker.stdout}).on('line',s=>{const r=JSON.parse(s),p=pending.get(r.id);if(p){clearTimeout(p.timer);pending.delete(r.id);r.ok?p.resolve(r.data):p.reject(Error(r.error.message))}});
const rpc=(method,payload)=>new Promise((resolve,reject)=>{const id=String(++serial),timer=setTimeout(()=>reject(Error('Timeout '+method)),180000);pending.set(id,{resolve,reject,timer});worker.stdin.write(JSON.stringify({id,method,payload})+'\n')});
(async()=>{try{
 const loaded=await rpc('load',{paths:fs.readdirSync(source).filter(n=>/\.db$/i.test(n)).map(n=>path.join(source,n))});
 console.log('Loaded',loaded.count);
 const rows=(await rpc('list',{type:'Texture2D'})).items;
 let tested=false;
 for(const row of rows.slice(0,30)){
  const explicit=await rpc('export',{assetIds:[row.id],format:'png',output:path.join(temp,'explicit')});
  if(!explicit.failed)continue;
  assert.equal(explicit.success,0);assert.equal(explicit.rawFallback,0);
  const automatic=await rpc('export',{assetIds:[row.id],format:'auto',output:path.join(temp,'automatic')});
  assert.equal(automatic.success,1);assert.equal(automatic.rawFallback,1);
  assert.equal(automatic.results[0].format,'raw');assert(automatic.results[0].warning);
  const raw=await rpc('export',{assetIds:[row.id],format:'raw',output:path.join(temp,'raw')});
  assert(fs.readFileSync(automatic.results[0].path).equals(fs.readFileSync(raw.results[0].path)));
  console.log(JSON.stringify({row,explicit,automatic,byteExact:true}));tested=true;break;
 }
 assert(tested,'Sample has no failing texture among first 30; use a package with a missing external image resource');
 console.log('PASS explicit conversion remains failed; auto fallback is marked and byte-exact');
}finally{worker.kill();for(const p of pending.values())clearTimeout(p.timer)}})().catch(e=>{console.error(e);process.exitCode=1});
