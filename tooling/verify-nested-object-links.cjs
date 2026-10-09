// Test the nested table identity layout against actual object evidence.
// Matching integers alone are candidates; an exact SerializedFile + PathID is required.
const fs=require('node:fs'),path=require('node:path'),rl=require('node:readline'),read=require('./qts-kv-reader.cjs');
const base=path.resolve(process.argv[2]);
const payload=id=>read(fs.readFileSync(path.join(base,'probes','nested-'+id,id+'.payload')));
const entries=payload('6167850575042422745'),locations=payload('8071403838198930324');
const locationByKey=new Map();
for(const r of locations){const key=r.key.toString('hex');if(!locationByKey.has(key))locationByKey.set(key,[]);locationByKey.get(key).push(r);}
const candidates=[];
for(const r of entries){
 if(r.key.length!==8||r.value.length<16||r.value.length%8)throw Error('Invalid resource-map record layout at '+r.offset);
 for(const location of locationByKey.get(r.value.subarray(0,8).toString('hex'))??[]){
  if(location.value.length!==16)throw Error('Invalid location-table value at '+location.offset);
  for(let at=8;at<r.value.length;at+=8)candidates.push({resourceKey:r.key.toString('hex'),recordOffset:r.offset,valueHex:r.value.toString('hex'),pathIdValueOffset:at,locationKey:location.key.toString('hex'),entry:location.value.readBigUInt64LE().toString(),sfOffset:location.value.readUInt32LE(8),sfLength:location.value.readUInt32LE(12),pathId:r.value.readBigInt64LE(at).toString(),found:false});
 }
}
(async()=>{
 const byIdentity=new Map();for(const c of candidates){const key=c.entry+':'+c.sfOffset+':'+c.pathId;if(!byIdentity.has(key))byIdentity.set(key,[]);byIdentity.get(key).push(c);}
 const matches=[],allPathIds=new Set(),sfIdentities=new Set();
 for await(const line of rl.createInterface({input:fs.createReadStream(path.join(base,'typetree-0','objects.jsonl'))})){
  const r=JSON.parse(line);
  if(r.kind==='serializedFile')sfIdentities.add(r.entry+':'+r.sfOffset);
  if(r.kind!=='object')continue;
  allPathIds.add(r.pathId);
  for(const c of byIdentity.get(r.entry+':'+r.sfOffset+':'+r.pathId)??[]){c.found=true;matches.push({...c,assetId:r.id,name:r.name,type:r.type,sf:r.sf});}
 }
 const result={generated:new Date().toISOString(),entries:entries.length,locations:locations.length,candidates:candidates.length,exactObjectMatches:matches.length,pathIdMatchesAnywhere:candidates.filter(c=>allPathIds.has(c.pathId)).length,serializedFileMatches:candidates.filter(c=>sfIdentities.has(c.entry+':'+c.sfOffset)).length,matches,unmatched:candidates.filter(c=>!c.found),scope:'Layout hypothesis validation only; no automatic GUID identity or dependency promotion.'};
 fs.writeFileSync(path.join(base,'nested-object-links.json'),JSON.stringify(result,null,2));console.log(JSON.stringify({...result,matches:matches.slice(0,5),unmatched:result.unmatched.slice(0,5)},null,2));
})().catch(e=>{console.error(e);process.exitCode=1});
