// Compare a full deep audit against its retained shallow scan. No game DB is
// loaded here and no raw/partial object is promoted to a successful decode.
const fs=require('node:fs'),path=require('node:path'),rl=require('node:readline'),assert=require('node:assert/strict');
const [beforeArg,afterArg,outputArg]=process.argv.slice(2);
if(!outputArg)throw Error('Usage: validate-deep-evidence <previous-jsonl> <deep-jsonl> <report-json>');
async function read(file,onObject){let source,summary,nonNullReferences=0;for await(const line of rl.createInterface({input:fs.createReadStream(file),crlfDelay:Infinity})){const row=JSON.parse(line);if(row.kind==='source')source=row;else if(row.kind==='summary')summary=row;else if(row.kind==='object'){onObject(row);nonNullReferences+=(row.references??[]).filter(r=>r.pathId!=='0').length;}}assert(source&&summary,'Missing complete evidence boundaries');return{source,summary,nonNullReferences};}
(async()=>{
 const before=new Map(),earlier=await read(beforeArg,r=>{assert(!before.has(r.id));assert.match(r.sha256??'',/^[a-f0-9]{64}$/i,'Previous object hash unavailable');before.set(r.id,{sha:r.sha256,status:r.parseStatus});});
 const remaining=new Set(before.keys()),transitions={},incomplete=[];
 const latest=await read(afterArg,r=>{
  const old=before.get(r.id);assert(old,'Deep scan introduced an unexpected object identity: '+r.id);assert(remaining.delete(r.id),'Duplicate deep object');assert.equal(r.sha256,old.sha,'Original bytes changed: '+r.id);
  const transition=old.status+' -> '+r.parseStatus;transitions[transition]=(transitions[transition]??0)+1;
  if(r.parseStatus!=='typed-complete'||r.remaining!==0||r.issues.length)incomplete.push({id:r.id,type:r.type,status:r.parseStatus,remaining:r.remaining,issues:r.issues});
 });
 assert.equal(latest.source.deep,true,'Latest evidence must be explicit deep mode');assert.equal(latest.source.sha256,earlier.source.sha256);assert.equal(latest.source.source,earlier.source.source);assert.equal(remaining.size,0,'Objects disappeared during deep scan');assert.equal(latest.summary.objects,before.size);
 const result={source:latest.source.source,sourceSha256:latest.source.sha256,before:path.resolve(beforeArg),after:path.resolve(afterArg),unchangedObjectHashes:before.size,transitions,referencesBefore:earlier.summary.references,referencesAfter:latest.summary.references,nonNullReferencesBefore:earlier.nonNullReferences,nonNullReferencesAfter:latest.nonNullReferences,incomplete,allObjectsFullyRead:incomplete.length===0,semanticComplete:false,note:'Reference totals include null slots; non-null references are separately counted and are not necessarily confirmed links. Full bounded object reads and unchanged bytes do not establish runtime closure or export/reimport completeness.'};
 fs.writeFileSync(outputArg,JSON.stringify(result,null,2));console.log(JSON.stringify({...result,incomplete:incomplete.length}));
})().catch(e=>{console.error(e);process.exitCode=1;});
