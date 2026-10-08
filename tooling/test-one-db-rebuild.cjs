// Verify an unchanged-container replacement through the new lazy storage path.
// One selected input DB; complete-package companions are preserved by rebuild.
const fs=require('node:fs'),path=require('node:path'),cp=require('node:child_process'),rl=require('node:readline'),assert=require('node:assert/strict'),crypto=require('node:crypto');
const [build,source,out]=process.argv.slice(2).map(p=>path.resolve(p));
const corpus=fs.readFileSync(path.join(__dirname,'../tests/hok_db_corpus.txt'),'utf8').trim().split(/\r?\n/);assert(corpus.some(d=>path.resolve(d).toLowerCase()===path.dirname(source).toLowerCase()));
const hash=f=>crypto.createHash('sha256').update(fs.readFileSync(f)).digest('hex');fs.mkdirSync(out,{recursive:true});
const originals=Object.fromEntries(fs.readdirSync(path.dirname(source)).filter(n=>fs.statSync(path.join(path.dirname(source),n)).isFile()).map(n=>[n,hash(path.join(path.dirname(source),n))]));
function worker(label){const proc=cp.spawn(path.join(build,'worker/Hok.Worker.exe'),[path.join(out,label)],{cwd:path.join(build,'worker'),windowsHide:true});proc.stderr.pipe(fs.createWriteStream(path.join(out,label+'.log')));let seq=0;const pending=new Map();rl.createInterface({input:proc.stdout}).on('line',line=>{const r=JSON.parse(line),p=pending.get(r.id);if(p){clearTimeout(p.timer);pending.delete(r.id);r.ok?p.ok(r.data):p.no(Error(r.error.message))}});return{proc,rpc:(method,payload={})=>new Promise((ok,no)=>{const id=String(++seq),timer=setTimeout(()=>{proc.kill();no(Error('Timeout '+method))},180000);pending.set(id,{ok,no,timer});proc.stdin.write(JSON.stringify({id,method,payload})+'\n')}),stop(){proc.kill();for(const p of pending.values())clearTimeout(p.timer)}}}
(async()=>{let w=worker('original');const report={source,checks:[]};try{
 const load=await w.rpc('load',{paths:[source]}),items=(await w.rpc('list',{type:'WwisePackage'})).items;assert(items.length>0);const asset=items[0];
 const exported=await w.rpc('export',{assetIds:[asset.id],format:'original',output:path.join(out,'original-export')});assert.equal(exported.failed,0);const file=exported.results[0].path;
 assert.equal((await w.rpc('replace',{assetId:asset.id,path:file})).count,1);assert.equal((await w.rpc('replace',{assetId:asset.id,path:file})).count,1);
 await w.rpc('releasePreview');assert.equal((await w.rpc('replacementState')).count,1);report.checks.push('lazy backing identity supports staging and repeat staging; preview release preserves replacements');
 const rebuilt=await w.rpc('rebuild',{output:out});const verification=JSON.parse(fs.readFileSync(rebuilt.reportPath));assert(verification.beta&&verification.completeDecodedContentsVerified&&!verification.gameCompatibilityVerified);report.checks.push('complete decoded contents checked and beta/game-compatibility status retained');
 for(const [name,sha] of Object.entries(originals)){assert.equal(hash(path.join(path.dirname(source),name)),sha);assert.equal(hash(path.join(rebuilt.output,name)),sha,'Unchanged replacement must preserve each companion byte-for-byte');}report.checks.push('all original DB and companion hashes unchanged; unchanged-container rebuild is byte-exact');
 w.stop();w=worker('reopened');const loaded=await w.rpc('load',{paths:[path.join(rebuilt.output,path.basename(source))]});assert.equal(loaded.count,load.count);report.checks.push('rebuilt DB reopens with the complete original row count');
 report.output=rebuilt.output;console.log(JSON.stringify(report));
 }catch(e){report.error=e.stack;throw e}finally{w.stop();fs.writeFileSync(path.join(out,'report.json'),JSON.stringify(report,null,2));}
})().catch(e=>{console.error(e);process.exitCode=1});
