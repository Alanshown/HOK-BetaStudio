const fs=require('node:fs'),path=require('node:path'),kv=require('./qts-kv-reader.cjs');
const base=path.resolve(process.argv[2]),read=id=>kv(fs.readFileSync(path.join(base,'probes','nested-'+id,id+'.payload')));
const entries=read('6167850575042422745'),blobs=read('8071403838198930324'),scripts=read('2254348891505130285');
const key=r=>r.key.toString('hex'),hist=list=>{const h={};for(const r of list){const k=r.key.length+':'+r.value.length;h[k]=(h[k]||0)+1;}return h;};
const entryKeys=new Set(entries.map(key)),entryValues=new Set(entries.map(r=>r.value.toString('hex'))),blobKeys=new Set(blobs.map(key)),scriptKeys=new Set(scripts.map(key));
let commonBlob=0,scriptGuids=0;for(const r of blobs)if(entryKeys.has(key(r)))commonBlob++;for(const r of entries)if(scriptKeys.has(r.value.toString('hex')))scriptGuids++;
const guid='9b04144aff86fa46b74295d474c3c427',hits=entries.filter(r=>r.value.toString('hex').includes(guid));
const linkCandidates=hits.map(r=>({key:key(r),keyUInt:r.key.readBigUInt64LE().toString(),value:r.value.toString('hex'),offset:r.offset,blob:blobs.filter(b=>key(b)===key(r)).map(b=>({offset:b.offset,value:b.value.toString('hex')}))}));
const result={counts:{entries:entries.length,blobs:blobs.length,scripts:scripts.length},layouts:{entries:hist(entries),blobs:hist(blobs),scripts:hist(scripts)},keyOverlap:{entriesWithBlobs:commonBlob,entryValuesWithScriptKeys:scriptGuids},knownMeshGuid:guid,linkCandidates,
 firstEntries:entries.slice(0,5).map(r=>({key:key(r),value:r.value.toString('hex')})),firstBlobs:blobs.slice(0,5).map(r=>({key:key(r),value:r.value.toString('hex')})),firstScripts:scripts.slice(0,5).map(r=>({key:key(r),value:r.value.toString('hex')}))};
fs.writeFileSync(path.join(base,'nested-mapping-analysis.json'),JSON.stringify(result,null,2));console.log(JSON.stringify(result,null,2));
