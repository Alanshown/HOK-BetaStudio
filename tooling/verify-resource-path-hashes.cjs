// Literal serialized strings only; report exact full-width matches separately.
const fs=require('node:fs'),path=require('node:path'),rl=require('node:readline'),read=require('./qts-kv-reader.cjs');
const base=path.resolve(process.argv[2]);
const entries=read(fs.readFileSync(path.join(base,'probes/nested-6167850575042422745/6167850575042422745.payload')));
const full=new Map(entries.map(r=>[r.key.toString('hex'),r])),low=new Map();for(const r of entries){const k=r.key.readUInt32LE();if(!low.has(k))low.set(k,[]);low.get(k).push(r);}
function hash(text){text=text.toLowerCase();let a=0x5bd1e995,b=0xab9423a7;for(let i=0;i<text.length;i++){a=(Math.imul(a,33)^text.charCodeAt(i))>>>0;b=(Math.imul(b,33)^text.charCodeAt(text.length-i-1))>>>0;}const raw=Buffer.alloc(8);raw.writeUInt32LE(a);raw.writeUInt32LE(b,4);return raw;}
(async()=>{
 const reports=fs.readdirSync(path.join(base,'db')).filter(f=>f.endsWith('.report.json')).map(f=>JSON.parse(fs.readFileSync(path.join(base,'db',f)))).filter(r=>r.ok&&/[\\/](0|3200014006)[\\/]/.test(r.source));
 const seen=new Set(),matches=[],lowOnly=[];let attempted=0;
 function test(value,origin){if(typeof value!=='string'||value.length>4096||!value.includes('/')||seen.has(value))return;seen.add(value);
  for(const candidate of new Set([value,value.replaceAll('\\','/'),value.replace(/\.[^/.]+$/,''),value.replace(/^assets\//i,''),value.replace(/^assets\/resources\//i,'')]))for(const logical of new Set([candidate,candidate.startsWith('/')?candidate.slice(1):'/'+candidate])){
   attempted++;const raw=hash(logical),key=raw.toString('hex'),record=full.get(key);if(record)matches.push({value,logical,key,valueHex:record.value.toString('hex'),origin});else if(low.has(raw.readUInt32LE()))for(const r of low.get(raw.readUInt32LE()))lowOnly.push({value,logical,key,recordKey:r.key.toString('hex'),valueHex:r.value.toString('hex'),origin});
  }
 }
 for(const report of reports)for await(const line of rl.createInterface({input:fs.createReadStream(report.output)})){
  const r=JSON.parse(line);if(r.kind!=='object')continue;for(const s of r.structuredStrings??[])test(s.value,{source:r.source,assetId:r.id,field:s.field,offset:s.serializedOffset});
 }
 const config=JSON.parse(fs.readFileSync(path.join(base,'configuration-graph.json')));for(const r of config.references)test(r.path,{source:r.source,entry:r.entry,kind:r.kind});
 const result={generated:new Date().toISOString(),strings:seen.size,attempted,exactMatches:matches.length,lowOnlyCount:lowOnly.length,matches,lowOnly,scope:'Low32 collisions never establish a path identity.'};fs.writeFileSync(path.join(base,'resource-path-hash-links.json'),JSON.stringify(result,null,2));console.log(JSON.stringify({...result,matches:matches.slice(0,8),lowOnly:lowOnly.slice(0,8)},null,2));
})().catch(e=>{console.error(e);process.exitCode=1});
