const fs=require('node:fs'),path=require('node:path'),{DatabaseSync}=require('node:sqlite');
const root=path.resolve(process.argv[2]),query=process.argv[3]||'14006',base=path.join(root,'evidence');
const db=new DatabaseSync(process.argv[4]?path.resolve(process.argv[4]):path.join(base,'objects.sqlite'),{readOnly:true});db.exec('PRAGMA cache_size=-131072');
const sources=db.prepare('SELECT * FROM sources WHERE complete=1').all(),byPath=new Map(sources.map(s=>[s.path,s])),sourceIds=new Set(sources.map(s=>s.id));
const selected=new Map(),queue=[],edges=[],reverse=[];
function add(row,reason){if(!row||!sourceIds.has(row.source))return;if(selected.has(row.id)){const old=selected.get(row.id);if(!old.reasons.includes(reason))old.reasons.push(reason);return;}if(selected.size>=50000)throw Error('Graph exceeded 50000 nodes: explicit investigation required');const node={...row,sourcePath:sources.find(s=>s.id===row.source).path,reasons:[reason]};selected.set(row.id,node);queue.push(node);}
const object=db.prepare('SELECT * FROM objects WHERE source=? AND asset_id=?'),sameSf=db.prepare('SELECT * FROM objects WHERE source=? AND sf=?'),pointers=db.prepare('SELECT * FROM refs WHERE owner=?'),targets=db.prepare('SELECT * FROM objects WHERE path_id=?'),incoming=db.prepare('SELECT o.*,r.owner,r.field FROM refs r JOIN objects o ON o.id=r.owner WHERE r.path_id=?');
const seed=sources.filter(s=>path.basename(path.dirname(s.path))==='32000'+query);
for(const s of seed)for(const row of db.prepare('SELECT * FROM objects WHERE source=?').all(s.id))add(row,'seed-package');
let candidateReports=0;
for(const filename of fs.readdirSync(path.join(base,'candidates')).filter(f=>f.endsWith('.json')&&f!=='summary.json')){
 const report=JSON.parse(fs.readFileSync(path.join(base,'candidates',filename)));const source=byPath.get(report.source);if(!source)continue;candidateReports++;
 for(const item of report.objects){const row=object.get(source.id,item.id);if(!row)continue;add(row,item.matchKind??'serialized-name-match');
  if(row.type==='GameObject')for(const sibling of sameSf.all(row.source,row.sf))add(sibling,'same-serialized-prefab-as-named-object');
  if(['Mesh','AnimationClip'].includes(row.type))for(const owner of incoming.all(row.path_id)){if(!sourceIds.has(owner.source))continue;reverse.push({target:row.id,owner:owner.id,field:owner.field,status:'reverse-PathID-candidate'});if(['MeshFilter','SkinnedMeshRenderer','Animation','AnimatorController','MonoBehaviour'].includes(owner.type)){add(owner,'reverse-use-of-named-asset');if(owner.type!=='AnimatorController')for(const sibling of sameSf.all(owner.source,owner.sf))add(sibling,'same-prefab-as-reverse-use');}}
 }
}
const aliases={Object:null,Texture:['Texture2D','Cubemap','RenderTexture','Texture3D','Texture2DArray','CubemapArray'],Transform:['Transform','RectTransform'],RuntimeAnimatorController:['AnimatorController','AnimatorOverrideController'],Renderer:['MeshRenderer','SkinnedMeshRenderer','ParticleSystemRenderer','TrailRenderer','LineRenderer','SpriteRenderer'],Component:['Animator','Animation','Transform','RectTransform','MeshFilter','MeshRenderer','SkinnedMeshRenderer','ParticleSystem','ParticleSystemRenderer','MonoBehaviour']};
function fits(expected,row){if(!expected||expected==='Object')return true;if(expected==='Component'&&aliases.Renderer.includes(row.type))return true;return (aliases[expected]||[expected]).includes(row.type);}
function qtsId(text){text=(text.startsWith('/')?text:'/'+text).toLowerCase();let a=0x5bd1e995,b=0xab9423a7;for(let i=0;i<text.length;i++){a=(Math.imul(a,33)^text.charCodeAt(i))>>>0;b=(Math.imul(b,33)^text.charCodeAt(text.length-i-1))>>>0;}return((BigInt(b)<<32n)|BigInt(a)).toString();}
for(let i=0;i<queue.length;i++){
 const owner=queue[i];for(const ref of pointers.all(owner.id)){
  if(ref.status==='builtin-resource'){edges.push({...ref,owner:owner.id,candidates:[],resolvedStatus:'builtin-resource'});continue;}
  let matches=targets.all(ref.path_id).filter(t=>sourceIds.has(t.source)&&fits(ref.expected_type,t)),status;
  if(ref.file_id===0){matches=matches.filter(t=>t.source===owner.source&&t.sf===owner.sf);status=matches.length===1?'confirmed':'missing';}
  else if(ref.target_sf){matches=matches.filter(t=>t.sf===ref.target_sf);status=matches.length===1?ref.status:matches.length?'conflict':'missing';}
  else{const explicit=ref.external_path&&!ref.external_path.startsWith('archive:/')?matches.filter(t=>t.entry===qtsId(ref.external_path)):[];
   if(explicit.length){matches=explicit;status=explicit.length===1?'confirmed':'conflict';}
   else{const local=matches.filter(t=>t.source===owner.source);if(local.length){matches=local;status=local.length===1?'local-candidate':'conflict';}else status=matches.length===1?'cross-db-candidate':matches.length?'conflict':'missing';}}
  edges.push({...ref,owner:owner.id,candidates:matches.map(t=>({id:t.id,source:t.source,sf:t.sf,pathId:t.path_id,type:t.type,name:t.name,sha256:t.sha256})),resolvedStatus:status,identityConfirmed:status==='confirmed'});
  if(matches.length===1)add(matches[0],status+' dependency of '+owner.id);
 }
}
const counts={};for(const edge of edges)counts[edge.resolvedStatus]=(counts[edge.resolvedStatus]||0)+1;
const inventory=JSON.parse(fs.readFileSync(path.join(base,'inventory.json'))),result={schema:1,query,generated:new Date().toISOString(),indexedSources:sources.length,plannedSources:inventory.total,libraryComplete:sources.length===inventory.total,semanticComplete:false,candidateReports,
 identityPolicy:'Cross-DB unique PathID and class matches are candidates, never proof of external GUID identity.',sources,nodes:[...selected.values()],edges,reverse,counts};
fs.writeFileSync(process.env.HOK_EVIDENCE_GRAPH?path.resolve(process.env.HOK_EVIDENCE_GRAPH):path.join(base,'dependency-graph.json'),JSON.stringify(result));
console.log(JSON.stringify({nodes:selected.size,edges:edges.length,counts,sources:[...new Set([...selected.values()].map(x=>x.sourcePath))],indexedSources:sources.length}));db.close();
