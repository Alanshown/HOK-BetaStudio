// Materialize completed per-DB evidence, never load a game DB here. Node 22.13+.
// PathIDs and QTS IDs are TEXT: converting either to JS Number loses identity.
const fs=require('node:fs'),path=require('node:path'),{DatabaseSync}=require('node:sqlite');
const root=path.resolve(process.argv[2]||'.'),reports=path.join(root,'evidence','db');
const database=process.argv[3]?path.resolve(process.argv[3]):path.join(root,'evidence','objects.sqlite');
const onlySource=process.argv[4];
fs.mkdirSync(path.dirname(database),{recursive:true});const db=new DatabaseSync(database);
db.exec(`PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA foreign_keys=ON; PRAGMA cache_size=-131072;
CREATE TABLE IF NOT EXISTS sources(id INTEGER PRIMARY KEY,path TEXT UNIQUE,sha256 TEXT,evidence TEXT,complete INTEGER DEFAULT 0,summary TEXT);
CREATE TABLE IF NOT EXISTS objects(id INTEGER PRIMARY KEY,source INTEGER REFERENCES sources ON DELETE CASCADE,asset_id TEXT,sf TEXT,entry TEXT,sf_offset INTEGER,path_id TEXT,class_id INTEGER,type TEXT,name TEXT,sha256 TEXT,type_hash TEXT,script_id TEXT,status TEXT,remaining INTEGER,coverage TEXT,line_offset INTEGER,line_bytes INTEGER,UNIQUE(source,asset_id));
CREATE TABLE IF NOT EXISTS refs(id INTEGER PRIMARY KEY,owner INTEGER REFERENCES objects ON DELETE CASCADE,field TEXT,file_id INTEGER,path_id TEXT,expected_type TEXT,guid_bytes TEXT,external_path TEXT,status TEXT,reason TEXT,target_sf TEXT,byte_offset INTEGER,object_offset INTEGER,raw_hex TEXT,origin TEXT);
CREATE TABLE IF NOT EXISTS entries(source INTEGER REFERENCES sources ON DELETE CASCADE,entry TEXT,kind TEXT,status TEXT,complete INTEGER,bytes INTEGER,error TEXT,PRIMARY KEY(source,entry));
CREATE TABLE IF NOT EXISTS hints(source INTEGER REFERENCES sources ON DELETE CASCADE,entry TEXT,offset INTEGER,encoding TEXT,context TEXT,evidence TEXT);
CREATE TABLE IF NOT EXISTS structured_strings(owner INTEGER REFERENCES objects ON DELETE CASCADE,field TEXT,value TEXT,byte_offset INTEGER,object_offset INTEGER,payload_offset INTEGER,evidence TEXT);
CREATE INDEX IF NOT EXISTS objects_path ON objects(path_id);
CREATE INDEX IF NOT EXISTS objects_source_sf ON objects(source,sf);
CREATE INDEX IF NOT EXISTS objects_script ON objects(script_id,type_hash);
CREATE INDEX IF NOT EXISTS refs_path ON refs(path_id);
CREATE INDEX IF NOT EXISTS refs_owner ON refs(owner);
CREATE INDEX IF NOT EXISTS entries_resource_id ON entries(entry);
CREATE INDEX IF NOT EXISTS hints_source ON hints(source);
CREATE INDEX IF NOT EXISTS structured_strings_owner ON structured_strings(owner);
`);
const insertObject=db.prepare('INSERT INTO objects(source,asset_id,sf,entry,sf_offset,path_id,class_id,type,name,sha256,type_hash,script_id,status,remaining,coverage,line_offset,line_bytes) VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)');
const insertRef=db.prepare('INSERT INTO refs(owner,field,file_id,path_id,expected_type,guid_bytes,external_path,status,reason,target_sf,byte_offset,object_offset,raw_hex,origin) VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?)');
const insertEntry=db.prepare('INSERT INTO entries VALUES(?,?,?,?,?,?,?)');
const insertHint=db.prepare('INSERT INTO hints VALUES(?,?,?,?,?,?)');
const insertString=db.prepare('INSERT INTO structured_strings VALUES(?,?,?,?,?,?,?)');
async function* lines(file){
 let pending=Buffer.alloc(0),offset=0;
 for await(const chunk of fs.createReadStream(file,{highWaterMark:1024*1024})){
  const buffer=pending.length?Buffer.concat([pending,chunk]):chunk;let begin=0,at;
  while((at=buffer.indexOf(10,begin))>=0){yield {text:buffer.toString('utf8',begin,at),offset:offset+begin,bytes:at-begin+1};begin=at+1;}
  pending=Buffer.from(buffer.subarray(begin));offset+=begin;
 }
 if(pending.length)throw Error('Evidence JSONL lacks final newline: '+file);
}
async function ingest(report){
 const old=db.prepare('SELECT * FROM sources WHERE path=?').get(report.source);
 if(old?.complete&&old.evidence===report.output)return false;
 if(old){console.log(JSON.stringify({source:report.source,stage:'replace-stale-evidence',previousEvidence:old.evidence}));db.prepare('DELETE FROM sources WHERE id=?').run(old.id);}
 const sid=Number(db.prepare('INSERT INTO sources(path,sha256,evidence) VALUES(?,?,?)').run(report.source,report.indexed.sourceSha256,report.output).lastInsertRowid);
 let objects=0,references=0,summary=null;
 db.exec('BEGIN');
 try{
  for await(const line of lines(report.output)){
   const r=JSON.parse(line.text);
   if(r.kind==='source'&&(r.source!==report.source||r.sha256!==report.indexed.sourceSha256))throw Error('Evidence source identity mismatch');
   if(r.kind==='object'){
    const oid=Number(insertObject.run(sid,r.id,r.sf,String(r.entry),r.sfOffset,r.pathId,r.classId,r.type,r.name,r.sha256,r.typeHash??null,r.scriptId??null,r.parseStatus,r.remaining??null,r.referenceCoverage,line.offset,line.bytes).lastInsertRowid);
    for(const ref of r.references){references++;if(ref.pathId==='0')continue;insertRef.run(oid,ref.field,ref.fileId,ref.pathId,ref.expectedType,ref.guidBytes??null,ref.externalPath??null,ref.status,ref.reason,ref.target??null,ref.serializedOffset??null,ref.objectOffset??null,ref.rawHex??null,ref.origin);}
    for(const text of r.structuredStrings??[])insertString.run(oid,text.field,text.value,text.serializedOffset,text.objectOffset,text.payloadOffset,text.evidence);
    objects++;if(objects%10000===0){db.exec('COMMIT; BEGIN');}if(objects%100000===0)console.log(JSON.stringify({source:report.source,stage:'ingest',objects,references}));
   }else if(r.kind==='entry'){
    insertEntry.run(sid,r.id,r.payloadKind,r.parseStatus,Number(r.complete),r.payloadBytes,r.error??null);
    for(const hint of r.queryHints)insertHint.run(sid,r.id,hint.offset??null,hint.encoding??null,hint.context??null,JSON.stringify(hint));
   }else if(r.kind==='summary')summary=r;
  }
  if(!summary||summary.objects!==objects||summary.references!==references)throw Error('Evidence totals mismatch');
  db.prepare('UPDATE sources SET complete=1,summary=? WHERE id=?').run(JSON.stringify(summary),sid);db.exec('COMMIT');
  console.log(JSON.stringify({source:report.source,objects,references,sqlite:true}));return true;
 }catch(e){
  // SQLite may already have rolled back an I/O/OOM failure. Do not replace
  // that actionable error with the secondary "no transaction" exception.
  try{db.exec('ROLLBACK');}catch(rollbackError){e.rollbackError=rollbackError.message;}
  throw e;
 }
}
(async()=>{
 for(const filename of fs.readdirSync(reports).filter(f=>f.endsWith('.report.json'))){
  const report=JSON.parse(fs.readFileSync(path.join(reports,filename),'utf8'));
  if(onlySource&&path.basename(report.source).toLowerCase()!==onlySource.toLowerCase())continue;
  if(report.ok&&fs.existsSync(report.output))await ingest(report);
 }
 console.log(JSON.stringify({sources:db.prepare('SELECT count(*) n FROM sources WHERE complete=1').get().n,objects:db.prepare('SELECT count(*) n FROM objects').get().n,nonNullReferences:db.prepare('SELECT count(*) n FROM refs').get().n}));
 db.exec('PRAGMA wal_checkpoint(TRUNCATE)');db.close();
})().catch(e=>{console.error(e);try{db.close()}catch{}process.exitCode=1});
