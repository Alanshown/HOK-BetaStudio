const fs=require('node:fs'),path=require('node:path'),{DatabaseSync}=require('node:sqlite');
const {xmlReferences}=require('./configuration-references.cjs');
const [rootArg,dbArg]=process.argv.slice(2),root=path.resolve(rootArg),base=path.join(root,'evidence'),db=new DatabaseSync(path.resolve(dbArg),{readOnly:true});
const lookup=db.prepare('SELECT e.*,s.path source,s.sha256 sourceSha FROM entries e JOIN sources s ON s.id=e.source WHERE e.entry=? AND s.complete=1');
function hash(text){text='/'+text.toLowerCase();let a=0x5bd1e995,b=0xab9423a7;for(let i=0;i<text.length;i++){a=(Math.imul(a,33)^text.charCodeAt(i))>>>0;b=(Math.imul(b,33)^text.charCodeAt(text.length-i-1))>>>0;}return((BigInt(b)<<32n)|BigInt(a)).toString();}
const reports=JSON.parse(fs.readFileSync(path.join(base,'structured-exports.json'))),documents=[],references=[];
for(const report of reports){const file=report.results.find(r=>r.format==='json').path,data=JSON.parse(fs.readFileSync(file));
 documents.push({source:report.source,entry:report.entry,kind:report.kind,export:file});
 if(report.kind==='XmlAsset')for(const ref of xmlReferences(data))references.push({source:report.source,entry:report.entry,kind:report.kind,...ref});
 if(report.kind==='HokActionTimeline')for(const event of data.events||[])for(const ref of event.embeddedLengthPrefixedStrings||[])if(ref.value.includes('/'))references.push({source:report.source,entry:report.entry,kind:report.kind,path:ref.value,eventHash:event.typeHash,offset:ref.offset,evidence:'unverified event-parameter string; enclosing parameter schema unknown'});
 if(report.kind==='StdrTable')for(const record of data.records)if(record.firstWord===14006||record.stringReferences.some(r=>/(^|[^0-9])14006([^0-9]|$)|GuanYu_Skin_C/.test(r.text))){
  documents.at(-1).matchedRecords??=[];documents.at(-1).matchedRecords.push(record);
  for(const ref of record.stringReferences)if(ref.text.includes('/')&&!/^https?:/.test(ref.text))references.push({source:report.source,entry:report.entry,kind:report.kind,path:ref.text,record:record.index,firstWord:record.firstWord,...ref});
 }
}
const attempts=new Map();
for(const ref of references){
 if(ref.referenceKind==='audio-event-name'){
  ref.targets=[];ref.resolutionStatus='symbolic-audio-event-unresolved';
  ref.resolutionNote='Resolve against audio event/bank metadata; do not hash this symbolic name as a QTS filesystem path.';continue;
 }
 let found=[];for(const prefix of ['', 'assets/','assets/resources/','assets/resource/','assets/assetbundles/','assets/res/','resources/'])for(const extension of ['', '.prefab','.xml','.action','.asset','.bytes']){
  const logical=prefix+ref.path+extension;if(!attempts.has(logical)){const id=hash(logical);attempts.set(logical,{id,matches:lookup.all(id)});}
  const result=attempts.get(logical);if(result.matches.length)found.push({logicalPath:logical,qtsId:result.id,pathRule:prefix||extension?'prefix/suffix hypothesis with exact QTS ID hit':'literal configured path with project hash',matches:result.matches});
 }
 ref.targets=found;
}
const result={schema:1,semanticComplete:false,documents,references,attempts:attempts.size,
 symbolicAudioCalls:references.filter(r=>r.referenceKind==='audio-event-name').length,
 uniqueAudioEventNames:[...new Set(references.filter(r=>r.referenceKind==='audio-event-name').map(r=>r.path))],
 note:'Literal configuration fields, validated table string handles and speculative binary string hints are distinguished. Exact QTS hits do not themselves establish Unity GUID identity or full runtime branch closure.'};
fs.writeFileSync(path.join(base,'configuration-graph.json'),JSON.stringify(result,null,2));
console.log(JSON.stringify({documents:documents.length,references:references.length,symbolicAudioCalls:result.symbolicAudioCalls,uniqueAudioEventNames:result.uniqueAudioEventNames.length,matched:references.filter(r=>r.targets.length).length,targets:references.filter(r=>r.targets.length).slice(0,5)},null,2));db.close();
