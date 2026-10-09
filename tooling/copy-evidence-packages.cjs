const fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto');
const root=path.resolve(process.argv[2]),evidence=path.join(root,'evidence'),inventory=JSON.parse(fs.readFileSync(path.join(evidence,'inventory.json'))),input=path.resolve(inventory.input);
const graph=JSON.parse(fs.readFileSync(process.env.HOK_EVIDENCE_GRAPH?path.resolve(process.env.HOK_EVIDENCE_GRAPH):path.join(evidence,'dependency-graph.json'))),output=path.join(root,'db');
if(output===input||output.startsWith(input+path.sep))throw Error('Copy output overlaps original corpus');
const configurationFile=path.join(evidence,'configuration-graph.json'),configuration=fs.existsSync(configurationFile)?JSON.parse(fs.readFileSync(configurationFile)):null;
const audioFile=path.join(evidence,'audio-event-bank-links.json'),audio=fs.existsSync(audioFile)?JSON.parse(fs.readFileSync(audioFile)):null;
const eventAudioSources=new Set((audio?.matches??[]).flatMap(m=>m.candidates.map(c=>c.source)));
const configuredAudioSources=new Set((audio?.configuredBankCandidates??[]).flatMap(m=>m.candidates.map(c=>c.source)));
const audioSources=new Set([...eventAudioSources,...configuredAudioSources]);
const requested=process.argv[3]?.split('|');
const folders=[...new Set([...graph.nodes.map(n=>path.dirname(n.sourcePath)),...(configuration?.documents??[]).map(d=>path.dirname(d.source)),...[...audioSources].map(s=>path.dirname(s))])].filter(f=>!requested||requested.includes(path.basename(f)));
function files(dir){return fs.readdirSync(dir,{withFileTypes:true}).flatMap(e=>{if(e.isSymbolicLink())throw Error('Package contains a reparse/symlink; inspect first: '+e.name);const file=path.join(dir,e.name);return e.isDirectory()?files(file):[file];});}
async function hash(file){const h=crypto.createHash('sha256');for await(const chunk of fs.createReadStream(file))h.update(chunk);return h.digest('hex');}
(async()=>{
 const manifest=path.join(evidence,'copied-packages.json'),previous=fs.existsSync(manifest)?JSON.parse(fs.readFileSync(manifest)):null;
 const records=(previous?.records??[]).filter(r=>!folders.includes(path.dirname(r.source)));
 for(const folder of folders){
  if(!folder.startsWith(input+path.sep))throw Error('Source is outside corpus: '+folder);
  const reasons=[...new Set(graph.nodes.filter(n=>path.dirname(n.sourcePath)===folder).flatMap(n=>n.reasons))];if(configuration?.documents.some(d=>path.dirname(d.source)===folder))reasons.push('configuration-or-action-document');
  if([...eventAudioSources].some(s=>path.dirname(s)===folder))reasons.push('declared-audio-event-hash-to-bank-candidate');
  if([...configuredAudioSources].some(s=>path.dirname(s)===folder))reasons.push('configured-bank-name-hash-candidate');
  for(const source of files(folder)){
   const relative=path.relative(input,source),dest=path.resolve(output,relative);
   if(!dest.startsWith(output+path.sep))throw Error('Unsafe relative destination');
   const before=fs.statSync(source),sourceHash=await hash(source);fs.mkdirSync(path.dirname(dest),{recursive:true});
   if(!fs.existsSync(dest))await fs.promises.copyFile(source,dest,fs.constants.COPYFILE_EXCL);
   const targetHash=await hash(dest),after=fs.statSync(source);
   if(before.size!==after.size||before.mtimeMs!==after.mtimeMs||sourceHash!==targetHash)throw Error('Copy/source identity mismatch: '+source);
   records.push({source,path:dest,bytes:before.size,sha256:sourceHash,originalPreserved:true,reasons});
  }
  console.log(JSON.stringify({copiedPackage:path.relative(input,folder),files:records.filter(r=>path.dirname(r.source)===folder).length,classification:reasons.some(r=>r==='serialized-name-match')?'contains named 14006 assets':reasons.includes('seed-package')?'seed SHOW package':'shared dependency candidate; consult graph'}));
 }
 fs.writeFileSync(path.join(evidence,'copied-packages.json'),JSON.stringify({generated:new Date().toISOString(),graphIndexedSources:graph.indexedSources,originalsModified:false,completeCorpusCollection:false,records},null,2));
})().catch(e=>{console.error(e);process.exitCode=1});
