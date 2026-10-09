const fs=require('node:fs'),path=require('node:path');
const root=path.resolve(__dirname,'..'),r=JSON.parse(fs.readFileSync(path.join(root,'planning/mapping-audit-report.json'),'utf8'));
const byPath=new Map();for(const o of r.objectInventory){if(!byPath.has(o.pathId))byPath.set(o.pathId,[]);byPath.get(o.pathId).push(o);}
const groups=new Map(),matches=[];
for(const p of r.pointers.filter(p=>p.targetPathId!=='0')){
 const candidates=byPath.get(p.targetPathId)||[],same=candidates.filter(o=>o.file===p.sourceFile);
 matches.push({...p,candidates:candidates.map(o=>({file:o.file,type:o.type,name:o.name})),sameFileCandidates:same.length});
 const key=p.externalGuid||'local';if(!groups.has(key))groups.set(key,{guid:p.externalGuid,path:p.externalPath,references:0,matchingFileCounts:{},missingTargets:0,ambiguousTargets:0});
 const group=groups.get(key);group.references++;if(!candidates.length)group.missingTargets++;if(candidates.length>1)group.ambiguousTargets++;
 for(const o of candidates)group.matchingFileCounts[o.file]=(group.matchingFileCounts[o.file]||0)+1;
}
const externalRows=r.files.flatMap(f=>f.externals),uniqueGuid=[...new Set(externalRows.map(e=>e.guid))];
const mapperCandidates=r.monoBehaviours.filter(m=>m.bytes>10000).map(m=>({file:m.file,pathId:m.pathId,bytes:m.bytes,scriptPathId:m.scriptPathId,scriptResolved:m.scriptResolved,strings:m.strings.length,shortExamples:m.strings.filter(s=>s.length<200).slice(0,20),interpretation:'Shader names/keywords/pass and rendering-cache-looking records; not a proven global call table.'}));
const summary={
 date:new Date().toISOString(),scope:'Read-only findings; candidate PathID matches are evidence for research, not an implemented GUID resolver.',
 inventory:r.counts,qts:r.qts.map(q=>({file:q.file,ids:q.entries.length,payloadIds:q.entries.filter(e=>e.chunks.some(c=>c.uncompressed>0)).length,header:q.header})),
 entryMembershipCounts:r.qts.map(q=>({file:q.file,onlyHere:q.entries.filter(e=>r.entryMembership[e.id].length===1).map(e=>e.id)})),
 textureMapping:{matched:r.textureMappings.filter(t=>t.exists&&t.wholeStream).length,total:r.textureMappings.length,example:r.textureMappings[0]},
 externalRefs:{rows:externalRows.length,uniqueGuids:uniqueGuid.length,blankPaths:externalRows.filter(e=>!e.pathName).length,named:[...new Set(externalRows.filter(e=>e.pathName).map(e=>e.pathName))]},
 currentResolver:{nonNull:matches.length,resolved:r.pointers.filter(p=>p.targetPathId!=='0'&&p.resolved).length,reason:'Legacy PPtr resolver searches external fileName; most GUID references have empty paths. These counts do not prove that the DB is broken.'},
 researchCandidates:{uniqueTargetMatches:matches.filter(m=>m.candidates.length===1).length,sameFileMatches:matches.filter(m=>m.sameFileCandidates===1).length,missingTargets:matches.filter(m=>m.candidates.length===0).length,ambiguous:matches.filter(m=>m.candidates.length>1).length,guidGroups:[...groups.values()]},
 rootCandidates:r.roots.map(t=>{const p=matches.find(p=>p.sourceFile===t.file&&p.sourcePathId===t.pathId&&p.field==='m_GameObject');return {...t,candidateGameObjects:p?.candidates}}),
 significantMonos:mapperCandidates,
 genericObjects:r.genericObjects.reduce((a,o)=>(a[o.type]=(a[o.type]||0)+1,a),{}),
 shaderVariants:r.genericObjects.filter(o=>o.type==='ShaderVariantCollection').map(o=>({...o,strings:o.strings.slice(0,14)})),
 record:r.record,
 absentTypes:['AssetBundle','ResourceManager','TextAsset','MonoScript','Material','AnimationClip','AudioClip'].filter(t=>!r.counts.types[t]),
 conclusions:['No single complete global mapping/call table was identified inside this folder.','File-ID/chunk tables and texture path hashes are directly verified mappings.','resourcesvolumecontext.asset is a named external dependency, not present in this supplied folder. Its contents/function are unverified.','Missing type trees and missing external/script resources prevent a complete behavioral call graph.'],
 inputHashesUnchanged:r.inputHashesUnchanged
};
fs.writeFileSync(path.join(root,'planning/mapping-audit-findings.json'),JSON.stringify(summary,null,2));
console.log(JSON.stringify({...summary,researchCandidates:{...summary.researchCandidates,guidGroups:summary.researchCandidates.guidGroups.slice(0,6)}},null,2));
