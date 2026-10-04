// End-to-end Windows worker regressions. Inputs are read-only; output stays in .cache.
const fs=require('fs'),path=require('path'),cp=require('child_process'),readline=require('readline'),assert=require('assert/strict'),crypto=require('crypto');
const root=path.resolve(__dirname,'..'),build=path.resolve(root,process.argv[2]),sample=path.resolve(process.argv[3]),fixtures=path.resolve(process.argv[4]);
const temp=fs.mkdtempSync(path.join(root,'.cache','discovery-worker-')),checks=[];
const pass=s=>{checks.push(s);console.log('PASS '+s)},sha=p=>crypto.createHash('sha256').update(fs.readFileSync(p)).digest('hex');
const worker=cp.spawn(path.join(build,'worker/Hok.Worker.exe'),[path.join(temp,'cache')],{cwd:path.join(build,'worker'),windowsHide:true});
const log=fs.createWriteStream(path.join(temp,'worker.log'));worker.stderr.pipe(log);
const pending=new Map();let serial=0;
readline.createInterface({input:worker.stdout}).on('line',s=>{const r=JSON.parse(s),p=pending.get(r.id);if(p){clearTimeout(p.timer);pending.delete(r.id);r.ok?p.resolve(r.data):p.reject(Error(r.error.message))}});
function rpc(method,payload){return new Promise((resolve,reject)=>{const id=String(++serial),timer=setTimeout(()=>{pending.delete(id);reject(Error('Timeout: '+method))},180000);pending.set(id,{resolve,reject,timer});worker.stdin.write(JSON.stringify({id,method,payload})+'\n')})}
async function main(){try{
 let load=await rpc('load',{paths:[path.join(fixtures,'broken.assets')]});assert.equal(load.objectTableRows,4);
 const failed=(await rpc('list',{type:'AnimationClip'})).items[0];assert.equal(failed.classId,74);assert.equal(failed.parseStatus,'parser-failed');assert.ok(failed.formats.includes('raw'));assert.equal(typeof failed.remainingBytes,'number');
 let out=await rpc('export',{assetIds:[failed.id],format:'raw',output:path.join(temp,'failed-raw')});assert.equal(out.failed,0);assert.deepEqual(fs.readFileSync(out.results[0].path),Buffer.alloc(4));pass('failed AnimationClip keeps category, counters and byte-exact raw export');
 load=await rpc('load',{paths:[path.join(fixtures,'many-errors.assets')]});assert.equal(load.errorCount,105);assert.equal(load.errors.length,100);assert.equal(load.errorsTruncated,true);assert.equal((await rpc('list',{type:'AnimationClip'})).total,105);pass('105 errors retain every row; truncated diagnostics explicitly marked');
 load=await rpc('load',{paths:[path.join(fixtures,'pages.assets')]});const pages=await Promise.all([0,1,2].map(page=>rpc('list',{page})));assert.deepEqual(pages.map(p=>p.items.length),[200,200,1]);pass('401 objects paginate without omission');
 for(const order of [['a','b'],['b','a']]){load=await rpc('load',{paths:order.map(n=>path.join(fixtures,n,'same.assets'))});const rows=(await rpc('list',{})).items;assert.equal(rows.length,2);assert.notEqual(rows[0].id,rows[1].id);assert.notEqual(rows[0].source,rows[1].source);}pass('identical basenames and PathIDs remain separate in either load order');
 const paths=fs.readdirSync(sample).filter(n=>n.endsWith('.db')).map(n=>path.join(sample,n)),hashes=paths.map(sha);
 load=await rpc('load',{paths});assert.equal(load.serializedFiles,196);assert.equal(load.objectTableRows,3685);assert.equal(load.types.AnimationClip,12);assert.equal(load.types.Texture2D,108);assert.equal(load.types.AnimatorController,8);assert.equal(load.errorCount,0);pass('real 19306 inventory agrees with source audit');
 for(const [type,format,count] of [['Mesh','obj',85],['Texture2D','png',108],['AnimationClip','anim',12]]){
  const rows=(await rpc('list',{type})).items;assert.equal(rows.length,count);out=await rpc('export',{assetIds:rows.map(r=>r.id),format,output:path.join(temp,format)});assert.equal(out.failed,0,JSON.stringify(out.results.filter(r=>!r.ok)));assert.equal(out.success,count);pass(`${count} ${type} exported as ${format}`);
 }
 const meta=(await rpc('list',{type:'PackageMetadata'})).items.find(r=>r.bytes==='116');assert.ok(meta);out=await rpc('export',{assetIds:[meta.id],format:'raw',output:path.join(temp,'metadata')});assert.equal(fs.statSync(out.results[0].path).size,116);pass('complete 116-byte package metadata exported');
 assert.deepEqual(paths.map(sha),hashes);pass('source SHA-256 unchanged');
 fs.writeFileSync(path.join(temp,'report.json'),JSON.stringify({passed:checks.length,checks,load,hashes},null,2));console.log('Report: '+path.join(temp,'report.json'));
}finally{worker.stdin.end();await new Promise(r=>worker.on('exit',r));log.end();}}
main().catch(e=>{console.error(e);process.exitCode=1});
