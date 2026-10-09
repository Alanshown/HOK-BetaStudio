const fs=require('node:fs'),path=require('node:path'),rl=require('node:readline'),read=require('./qts-kv-reader.cjs');
const base=path.resolve(process.argv[2]),payload=id=>read(fs.readFileSync(path.join(base,'probes','nested-'+id,id+'.payload')));
const entries=payload('6167850575042422745'),blobs=payload('8071403838198930324'),deps=payload('2254348891505130285');
const blobMap=new Map(blobs.map(r=>[r.key.readBigUInt64LE().toString(),r])),entryMap=new Map(entries.map(r=>[r.key.readBigUInt64LE().toString(),r]));
function h(text){text=text.toLowerCase();let a=0x5bd1e995,b=0xab9423a7;for(let i=0;i<text.length;i++){a=(Math.imul(a,33)^text.charCodeAt(i))>>>0;b=(Math.imul(b,33)^text.charCodeAt(text.length-i-1))>>>0;}return((BigInt(b)<<32n)|BigInt(a)).toString();}
const describe=r=>({offset:r.offset,key:r.key.toString('hex'),value:r.value.toString('hex')});
(async()=>{
 const source=path.join(base,'typetree-0/objects.jsonl'),samples=[],identities=new Set(),sfByOffset=new Map(),scripts=new Set(),externalHashHits=[],seenExternals=new Set();let pathIdsAsBlobKeys=0,qtsIdsAsEntryKeys=0;
 for await(const line of rl.createInterface({input:fs.createReadStream(source)})){const r=JSON.parse(line);if(r.kind==='serializedFile'){sfByOffset.set(r.entry+':'+r.sfOffset,r);for(const e of r.externals){if(e.guidBytes)identities.add(e.guidBytes.toLowerCase());const identity=JSON.stringify(e);if(seenExternals.has(identity))continue;seenExternals.add(identity);for(const s of new Set([e.guidBytes,e.guid?.replaceAll('-',''),e.guid,e.pathName,e.fileName].filter(Boolean)))for(const ext of ['','.asset','.prefab','.mat','.fbx','.controller','.anim','.png','.assets','.resS'])for(const logical of [s+ext,'/'+s+ext,'cab-'+s+ext,'/cab-'+s+ext]){const key=h(logical);if(blobMap.has(key)||entryMap.has(key))externalHashHits.push({external:e,logical,key,blob:blobMap.has(key)?describe(blobMap.get(key)):null,entry:entryMap.has(key)?describe(entryMap.get(key)):null});}}}
  if(r.kind==='object'){const unsigned=BigInt.asUintN(64,BigInt(r.pathId)).toString();if(blobMap.has(unsigned)){pathIdsAsBlobKeys++;if(samples.length<10)samples.push({object:r.name,class:r.type,pathId:r.pathId,entry:r.entry,sfOffset:r.sfOffset,record:describe(blobMap.get(unsigned))});}if(r.type==='MonoScript')scripts.add(unsigned);}
  if(r.kind==='entry'&&entryMap.has(r.id))qtsIdsAsEntryKeys++;
 }
 const blobRanges=blobs.slice(0,10).map(r=>{const entry=r.value.readBigUInt64LE().toString(),offset=r.value.readUInt32LE(8),length=r.value.readUInt32LE(12),sf=sfByOffset.get(entry+':'+offset);return{record:describe(r),entry,offset,length,sf:sf?.identity};});
 let scriptPointers=0,allPointers=0,entryGuidHits=0;for(const r of deps)for(let p=0;p<r.value.length;p+=8){allPointers++;if(scripts.has(r.value.readBigUInt64LE(p).toString()))scriptPointers++;}
 for(const r of entries)if(identities.has(r.value.toString('hex')))entryGuidHits++;
 const config=JSON.parse(fs.readFileSync(path.join(base,'configuration-graph.json'))),pathHits=[];
 for(const r of config.references)for(const prefix of ['','/','assets/','/assets/','assets/resources/','/assets/resources/','assets/resource/','/assets/resource/','assets/res/','/assets/res/','resources/','/resources/'])for(const ext of ['','.prefab','.asset','.xml','.bytes','.action']){
  const logical=prefix+r.path+ext,key=h(logical);if(entryMap.has(key)||blobMap.has(key))pathHits.push({path:r.path,logical,key,entry:entryMap.has(key)?describe(entryMap.get(key)):null,blob:blobMap.has(key)?describe(blobMap.get(key)):null});
 }
 const result={pathIdsAsBlobKeys,qtsIdsAsEntryKeys,scriptPointers,allPointers,entryGuidHits,externalHashHitCount:externalHashHits.length,externalHashHits:externalHashHits.slice(0,20),samples,blobRanges,pathHits};fs.writeFileSync(path.join(base,'nested-record-links.json'),JSON.stringify(result,null,2));console.log(JSON.stringify(result,null,2));
})();
