// Inspect existing evidence only. A missing stream hash is not a license to
// substitute a similarly named image or reinterpret Unity GUIDs as QTS IDs.
const fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto'),{DatabaseSync}=require('node:sqlite'),readKv=require('./qts-kv-reader.cjs');
const [rootArg,sqliteArg]=process.argv.slice(2),root=path.resolve(rootArg),base=path.join(root,'evidence');
const latest=new Map();for(const line of fs.readFileSync(path.join(base,'asset-exports.jsonl'),'utf8').split(/\r?\n/).filter(Boolean)){const r=JSON.parse(line);latest.set(r.source+'|'+r.id+'|'+r.format,r);}
const failed=[...latest.values()].filter(r=>!r.ok&&r.type==='Texture2D'&&r.format==='png'&&r.error.includes("Can't find the resource file"));
const maps=['6167850575042422745','8071403838198930324'].map(id=>({id,records:readKv(fs.readFileSync(path.join(base,'probes','nested-'+id,id+'.payload')))}));
for(const map of maps){map.byKey=new Map();for(const r of map.records){const key=r.key.toString('hex');if(!map.byKey.has(key))map.byKey.set(key,[]);map.byKey.get(key).push(r);}}
const db=new DatabaseSync(path.resolve(sqliteArg),{readOnly:true}),source=db.prepare('SELECT * FROM sources WHERE path=? AND complete=1'),object=db.prepare('SELECT * FROM objects WHERE source=? AND asset_id=?'),entry=db.prepare('SELECT s.path,e.* FROM entries e JOIN sources s ON s.id=e.source WHERE e.entry=? AND s.complete=1');
const records=[];for(const row of failed){const s=source.get(row.source),o=s&&object.get(s.id,row.id);if(!o)throw Error('Failed asset is absent from evidence: '+row.id);
 const fd=fs.openSync(s.evidence,'r'),bytes=Buffer.alloc(o.line_bytes);try{if(fs.readSync(fd,bytes,0,bytes.length,o.line_offset)!==bytes.length)throw Error('Short evidence read');}finally{fs.closeSync(fd);}
 const item=JSON.parse(bytes);if(!item.stream)throw Error('No explicit stream metadata for '+row.id);const raw=Buffer.alloc(8);raw.writeBigUInt64LE(BigInt(item.stream.qtsId));
 records.push({source:row.source,assetId:row.id,name:row.name,stream:item.stream,sourceObjectSha256:item.sha256,evidenceFile:s.evidence,evidenceOffset:o.line_offset,
  indexedQtsEntries:entry.all(item.stream.qtsId),nestedTableKeys:maps.flatMap(m=>(m.byKey.get(raw.toString('hex'))??[]).map(r=>({table:m.id,offset:r.offset,valueHex:r.value.toString('hex')}))),exportError:row.error});
}
const result={generated:new Date().toISOString(),failedTextures:records.length,indexedSources:db.prepare('SELECT count(*) n FROM sources WHERE complete=1').get().n,matchedIndexedEntryIds:records.filter(r=>r.indexedQtsEntries.length).length,matchedNestedTableKeys:records.filter(r=>r.nestedTableKeys.length).length,records,scope:'Exact declared stream QTS IDs against completed evidence and observed nested KV keys. No outside directory assumption, fuzzy name match, or complete-corpus absence claim.'};
fs.writeFileSync(path.join(base,'missing-stream-identity-check.json'),JSON.stringify(result,null,2));console.log(JSON.stringify({...result,records:undefined}));db.close();
