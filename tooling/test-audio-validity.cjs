// Exercise actual exported files, not just successful RPC responses or extensions.
const fs=require('node:fs'),path=require('node:path'),cp=require('node:child_process'),rl=require('node:readline'),assert=require('node:assert/strict'),crypto=require('node:crypto');
const root=path.resolve(__dirname,'..'),build=path.resolve(root,process.argv[2]),folders=process.argv.slice(3).map(p=>path.resolve(p));
assert(folders.length,'Provide a build folder and one or more DB folders');
const scratch=fs.mkdtempSync(path.join(root,'.cache','audio-validity-')),sha=b=>crypto.createHash('sha256').update(b).digest('hex');
const tool=n=>path.join(build,'worker/media',n);
function run(name,args){const r=cp.spawnSync(tool(name),args,{windowsHide:true,encoding:'utf8',timeout:120000,maxBuffer:4*1024*1024});assert.equal(r.status,0,name+': '+r.stderr+' '+r.stdout);return r.stdout;}
function chunks(bytes,from=0,end=bytes.length,riff=false){const found=[];let p=from;while(p<end){assert(p+8<=end,'Incomplete chunk header');const tag=bytes.toString('ascii',p,p+4),size=bytes.readUInt32LE(p+4);assert(p+8+size<=end,'Incomplete '+tag+' chunk');found.push({tag,begin:p+8,size});p+=8+size;if(riff&&(size&1)&&p<end)p++;}assert.equal(p,end);return found;}
function wave(file){const b=fs.readFileSync(file);assert.equal(b.toString('ascii',0,4),'RIFF');assert.equal(b.toString('ascii',8,12),'WAVE');assert.equal(b.readUInt32LE(4)+8,b.length);const list=chunks(b,12,b.length,true),fmt=list.find(x=>x.tag==='fmt '),data=list.find(x=>x.tag==='data');assert(fmt&&data&&data.size>0);const channels=b.readUInt16LE(fmt.begin+2),sampleRate=b.readUInt32LE(fmt.begin+4),align=b.readUInt16LE(fmt.begin+12),bits=b.readUInt16LE(fmt.begin+14);assert(channels>0&&sampleRate>0&&align>0);let peak=0;if(bits===16)for(let p=data.begin;p+2<=data.begin+data.size;p+=2)peak=Math.max(peak,Math.abs(b.readInt16LE(p)));return{channels,sampleRate,seconds:data.size/align/sampleRate,peak};}
async function check(source){
 const files=fs.statSync(source).isFile()?[source]:fs.readdirSync(source).filter(n=>/\.db$/i.test(n)).map(n=>path.join(source,n)),before=files.map(f=>sha(fs.readFileSync(f)));
 const dir=fs.mkdtempSync(path.join(scratch,'package-'));let serial=0;const pending=new Map(),worker=cp.spawn(path.join(build,'worker/Hok.Worker.exe'),[dir],{cwd:path.join(build,'worker'),windowsHide:true});
 worker.stderr.pipe(fs.createWriteStream(path.join(dir,'worker.log')));
 rl.createInterface({input:worker.stdout}).on('line',s=>{const r=JSON.parse(s),p=pending.get(r.id);if(p){clearTimeout(p.timer);pending.delete(r.id);r.ok?p.resolve(r.data):p.reject(Error(r.error.message))}});
 const rpc=(method,payload)=>new Promise((resolve,reject)=>{const id=String(++serial),timer=setTimeout(()=>reject(Error('Worker timeout '+method)),180000);pending.set(id,{resolve,reject,timer});worker.stdin.write(JSON.stringify({id,method,payload})+'\n')});
 const result={source,dir,banks:[],audio:[],errors:[]};
 try{
  const loaded=await rpc('load',{paths:files});console.log(JSON.stringify({source,count:loaded.count,types:loaded.types,errorCount:loaded.errorCount}));
  async function list(type){const all=[];let total=1;for(let page=0;all.length<total;page++){const r=await rpc('list',{type,page});total=r.total;all.push(...r.items);}return all;}
  async function exp(row,format){const r=await rpc('export',{assetIds:[row.id],format,output:path.join(dir,'exports')});assert.equal(r.failed,0,JSON.stringify(r));return r.results[0].path;}
  for(const bank of await list('WwiseBank')){try{
   const file=await exp(bank,'original'),bytes=fs.readFileSync(file),list=chunks(bytes);assert.equal(list[0].tag,'BKHD');assert(list[0].size>=4);
   const idx=list.find(c=>c.tag==='DIDX'),data=list.find(c=>c.tag==='DATA'),members=[];if(idx){assert(data);assert.equal(idx.size%12,0);for(let p=idx.begin;p<idx.begin+idx.size;p+=12){const id=bytes.readUInt32LE(p),offset=bytes.readUInt32LE(p+4),size=bytes.readUInt32LE(p+8);assert(offset+size<=data.size);members.push({id,bytes:size,sha256:sha(bytes.subarray(data.begin+offset,data.begin+offset+size))});}}
   const item={name:bank.name,file,version:bytes.readUInt32LE(list[0].begin),members,archives:[]};
   if(members.length){
    const metadata=JSON.parse(run('vgmstream-cli.exe',['-m','-I',file]));assert.equal(metadata.streamInfo.total,members.length);
    for(const format of ['zip-wem','zip-mp3']){
     const archive=await exp(bank,format),out=fs.mkdtempSync(path.join(dir,format+'-'));
     const unpack=cp.spawnSync('C:/Program Files/7-Zip/7z.exe',['x',archive,'-o'+out,'-y'],{windowsHide:true,encoding:'utf8'});assert.equal(unpack.status,0,unpack.stderr);
     const exported=fs.readdirSync(out);assert.equal(exported.length,members.length);
     for(const member of members){const entry=path.join(out,member.id+'.'+format.slice(4));assert(fs.existsSync(entry));if(format==='zip-wem')assert.equal(sha(fs.readFileSync(entry)),member.sha256);else run('ffmpeg.exe',['-nostdin','-v','error','-xerror','-i',entry,'-f','null','-']);}
     item.archives.push({format,file:archive,members:members.length,validated:true});
    }
   }
   result.banks.push(item);console.log(JSON.stringify({bank:bank.name,version:item.version,embedded:members.length,archives:item.archives}));
  }catch(e){result.errors.push({name:bank.name,error:e.message});}}
  for(const a of [...await list('WwiseAudio'),...await list('AudioFile')]){try{
   const file=await exp(a,a.formats.includes('wem')?'wem':'original'),b=fs.readFileSync(file);
   if(file.endsWith('.wem')){assert.equal(b.toString('ascii',0,4),'RIFF');assert.equal(b.readUInt32LE(4)+8,b.length,'WEM declared RIFF size');chunks(b,12,b.length,true);}
   assert.equal(path.basename(file),a.name,'Original Wwise filename preserved');
   const decoded=path.join(dir,'external-'+result.audio.length+'.wav');run('vgmstream-cli.exe',['-i','-o',decoded,file]);const info=wave(decoded);
   const preview=await rpc('preview',{assetId:a.id});const previewInfo=wave(path.join(dir,preview.file));assert.equal(previewInfo.seconds,info.seconds);
   const mp3=await exp(a,'mp3');assert.equal(path.basename(mp3),path.parse(a.name).name+'.mp3');run('ffmpeg.exe',['-nostdin','-v','error','-xerror','-i',mp3,'-f','null','-']);
   const contentHash=sha(b),id=Number(path.basename(a.name,'.wem')),bankMatches=result.banks.flatMap(b=>b.members).filter(m=>m.id===id);
   if(bankMatches.length)assert(bankMatches.some(m=>m.sha256===contentHash),'WEM output must equal the exact BNK DIDX byte range');
   const row={name:a.name,file,mp3,sha256:contentHash,bytes:b.length,...info};result.audio.push(row);console.log(JSON.stringify({audio:a.name,...info}));
  }catch(e){result.errors.push({name:a.name,error:e.message});console.log(JSON.stringify(result.errors.at(-1)));}}
  assert.deepEqual(files.map(f=>sha(fs.readFileSync(f))),before);result.originalsUnchanged=true;
 }finally{worker.kill();for(const p of pending.values())clearTimeout(p.timer);}
 fs.writeFileSync(path.join(dir,'report.json'),JSON.stringify(result,null,2));return result;
}
(async()=>{const results=[];for(const folder of folders)results.push(await check(folder));fs.writeFileSync(path.join(scratch,'report.json'),JSON.stringify(results,null,2));console.log(JSON.stringify({scratch,packages:results.map(r=>({source:r.source,banks:r.banks.length,audio:r.audio.length,errors:r.errors}))}));assert(results.every(r=>!r.errors.length),'Audio validation failed; inspect report');})().catch(e=>{console.error(e);process.exitCode=1});
