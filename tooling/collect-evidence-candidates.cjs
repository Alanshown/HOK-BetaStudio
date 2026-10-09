// Read completed JSONL indexes sequentially. This does not open/decode any DB.
const fs=require('node:fs'),path=require('node:path'),rl=require('node:readline');
const root=path.resolve(process.argv[2]),query=process.argv[3]||'14006',aliases=(process.argv[4]??'').split('|').filter(Boolean),onlySource=process.argv[5],base=path.join(root,'evidence'),dir=path.join(base,'db'),out=path.join(base,'candidates');
fs.mkdirSync(out,{recursive:true});
const escaped=query.replace(/[.*+?^${}()|[\]\\]/g,'\\$&'),nameHit=new RegExp('"name":"[^"\\n]*'+escaped,'i'),boundary=new RegExp('(^|[^0-9])'+escaped+'([^0-9]|$)','i');
const isMatch=value=>boundary.test(value)||aliases.some(a=>value.toLowerCase().includes(a.toLowerCase()));
(async()=>{const summaries=[];for(const report of fs.readdirSync(dir).filter(f=>f.endsWith('.report.json'))){
 const source=JSON.parse(fs.readFileSync(path.join(dir,report)));if(!source.ok)continue;
 if(onlySource&&path.basename(source.source).toLowerCase()!==onlySource.toLowerCase())continue;
 const dest=path.join(out,report.replace('.report.json','.json'));
 if(fs.existsSync(dest)){const old=JSON.parse(fs.readFileSync(dest));if(old.evidence===source.output&&JSON.stringify(old.aliases??[])===JSON.stringify(aliases)){summaries.push({source:old.source,objects:old.objects.length,entries:old.entries.length,file:dest});continue;}}
 const objects=[],entries=[];
 for await(const line of rl.createInterface({input:fs.createReadStream(source.output),crlfDelay:Infinity})){
  if(line.startsWith('{"kind":"object"')&&(line.includes(query)||aliases.some(a=>line.toLowerCase().includes(a.toLowerCase())))){const r=JSON.parse(line),strings=(r.structuredStrings??[]).filter(s=>isMatch(s.value));if(isMatch(r.name)||strings.length)objects.push({id:r.id,pathId:r.pathId,type:r.type,name:r.name,source:r.source,sf:r.sf,entry:r.entry,offset:r.sfOffset,sha256:r.sha256,parseStatus:r.parseStatus,matchKind:isMatch(r.name)?'serialized-name-match':'validated-script-string-candidate',structuredStrings:strings});}
  else if(line.startsWith('{"kind":"entry"')&&line.includes('"queryHints":[{')){const r=JSON.parse(line);entries.push({id:r.id,kind:r.payloadKind,objectCount:r.objectCount,hints:r.queryHints});}
 }
 const result={source:source.source,sourceSha:source.indexed.sourceSha256,evidence:source.output,query,aliases,objects,entries,classification:'discovery candidates; aliases must be supplied from verified skin configuration, names and byte hits do not prove battle use'};
 fs.writeFileSync(dest,JSON.stringify(result,null,2));summaries.push({source:source.source,objects:objects.length,entries:entries.length,file:dest});console.log(JSON.stringify(summaries.at(-1)));
 }
 if(!onlySource)fs.writeFileSync(path.join(out,'summary.json'),JSON.stringify(summaries,null,2));
})().catch(e=>{console.error(e);process.exitCode=1});
