const fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto'),{inspect,eventId}=require('./wwise-bank-evidence.cjs');
const root=path.resolve(process.argv[2]),base=path.join(root,'evidence'),config=JSON.parse(fs.readFileSync(path.join(base,'configuration-graph.json')));
const calls=config.references.filter(r=>r.referenceKind==='audio-event-name'),latest=new Map();
for(const line of fs.readFileSync(path.join(base,'asset-exports.jsonl'),'utf8').split(/\r?\n/).filter(Boolean)){const r=JSON.parse(line);if(r.type==='WwiseBank'&&r.format==='original')latest.set(r.source+'|'+r.id,r);}
const extra=path.join(base,'audio-bank-exports.jsonl');if(fs.existsSync(extra))for(const line of fs.readFileSync(extra,'utf8').split(/\r?\n/).filter(Boolean)){const r=JSON.parse(line);latest.set(r.source+'|'+r.id,r);}
const banks=[],failures=[];
for(const row of latest.values()){try{if(!row.ok)throw Error(row.error);const bytes=fs.readFileSync(row.path),sha256=crypto.createHash('sha256').update(bytes).digest('hex');if(sha256!==row.sha256)throw Error('Bank export hash changed');banks.push({source:row.source,assetId:row.id,path:row.path,sha256,...inspect(bytes)});}catch(e){failures.push({source:row.source,assetId:row.id,error:e.message});}}
const matches=calls.map(call=>({...call,eventId:eventId(call.path),candidates:banks.flatMap(bank=>bank.events.filter(event=>event.id===eventId(call.path)).map(event=>({source:bank.source,bankAssetId:bank.assetId,bankId:bank.bankId,bankPath:bank.path,bankSha256:bank.sha256,event}))),identityPolicy:'Exact declared event-name hash + HIRC Event ID; branch applicability, hash collisions and event/action/media closure require further validation'}));
// A skin configuration can name a whole bank (for example VO_OUT), without
// spelling out its Event names in the currently decoded action documents.
// Preserve the record/string evidence; a hash hit is still a candidate, not
// a guessed table-field definition or proof of runtime branch applicability.
const configuredBankCandidates=[];
for(const document of config.documents??[])for(const record of document.matchedRecords??[])for(const ref of record.stringReferences??[]){
 if(typeof ref.text!=='string'||!ref.text.length)continue;
 const bankId=eventId(ref.text),candidates=banks.filter(b=>b.bankId===bankId).map(b=>({source:b.source,bankAssetId:b.assetId,bankId:b.bankId,bankPath:b.path,bankSha256:b.sha256}));
 if(candidates.length)configuredBankCandidates.push({source:document.source,entry:document.entry,document:document.export,recordIndex:record.index,firstWord:record.firstWord,stringReference:ref,bankName:ref.text,bankId,candidates,referenceKind:'configured-bank-name-candidate',identityConfirmed:false,identityPolicy:'Exact validated configuration string hash + BKHD bank ID; table-field semantics, hash collisions and runtime applicability not established'});
}
const result={generated:new Date().toISOString(),semanticComplete:false,indexedBanks:banks.length,failures,declaredCalls:calls.length,uniqueEventNames:new Set(calls.map(c=>c.path)).size,matchedCalls:matches.filter(m=>m.candidates.length).length,unresolvedCalls:matches.filter(m=>!m.candidates.length).length,banks,matches,configuredBankCandidates,
 sources:['https://github.com/bnnm/wwiser/blob/master/wwiser/wfnv.py','https://github.com/bnnm/wwiser/blob/master/wwiser/parser/wparser.py']};
fs.writeFileSync(path.join(base,'audio-event-bank-links.json'),JSON.stringify(result,null,2));console.log(JSON.stringify({...result,banks:undefined,matches:undefined,configuredBankCandidates:configuredBankCandidates.length}));
