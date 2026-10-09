const fs=require('node:fs'),path=require('node:path');const root=path.resolve(__dirname,'..');const dirs=fs.readFileSync(path.join(root,'tests/hok_db_corpus.txt'),'utf8').split(/\r?\n/).filter(Boolean),results=[];
for(const dir of dirs)for(const name of fs.readdirSync(dir).filter(x=>x.endsWith('.db'))){
 const file=path.join(dir,name),fd=fs.openSync(file,'r'),size=fs.fstatSync(fd).size;
 const read=(o,n)=>{if(o<0||n<0||o+n>size)throw Error('Range '+o+' '+n);const b=Buffer.alloc(n);if(fs.readSync(fd,b,0,n,o)!==n)throw Error('Short');return b};
 const h=read(0,80),r={file,tables:[],blockTypes:{},blocks:0,entryOffsets:0,extraFromEntries1:0,extraFromLinks:0,errors:[],type2Samples:[]},all=new Set(),baseline=new Set(),seen=new Set(),queue=[];
 for(const at of [64,72,56]){const off=h.readInt32LE(at),length=h.readInt32LE(at+4);if(off<0)continue;try{const b=read(off,24),cap=b.readInt32LE(0),count=b.readInt32LE(4);if(count<0||cap<count||24+count*24>length)throw Error('Table count');r.tables.push({field:at,offset:off,length,cap,count});const table=read(off+24,count*24);for(let i=0;i<count;i++)for(let j=0;j<3;j++){const p=table.readInt32LE(i*24+j*4);if(p>0)queue.push({p,kind:at===56?'entries1':'baseline'})}}catch(e){r.errors.push({table:at,error:e.message})}}
 for(let qi=0;qi<queue.length;qi++){const {p,kind}=queue[qi];if(seen.has(p))continue;seen.add(p);try{const b=read(p,4096),count=b.readUInt16LE(4092),type=b.readUInt16LE(4094),next=b.readInt32LE(4088);if(count>510)throw Error('Block count '+count);r.blocks++;r.blockTypes[type]=(r.blockTypes[type]||0)+1;
  if(type===2){r.type2Samples.push({p,count,next,head:b.subarray(0,48).toString('hex'),offsets:b.subarray(2040,2088).toString('hex')});for(let i=0;i<count;i++){const x=b.readInt32LE(2040+i*4);if(x>0)queue.push({p:x,kind:'links'})}}
  else for(let i=0;i<count;i++){const x=b.readInt32LE(2040+i*4);if(x>0){if(!all.has(x)&&kind==='entries1')r.extraFromEntries1++;if(!all.has(x)&&kind==='links')r.extraFromLinks++;all.add(x);if(kind==='baseline')baseline.add(x)}}
  if(next>0&&next+4096<=size)queue.push({p:next,kind:'links'});
 }catch(e){r.errors.push({block:p,kind,error:e.message})}}
 r.entryOffsets=all.size;r.baselineOffsets=baseline.size;fs.closeSync(fd);results.push(r);console.log(name,JSON.stringify({blocks:r.blocks,types:r.blockTypes,extra1:r.extraFromEntries1,extraLinks:r.extraFromLinks,errors:r.errors.length}));
}
fs.writeFileSync(path.join(root,'reports/baseline/table-audit.json'),JSON.stringify(results,null,2));
