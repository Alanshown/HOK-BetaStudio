// Validate newly exported artifacts and that the semantic delta is rotation-only.
const fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto'),assert=require('node:assert/strict');
const [oldArg,newArg]=process.argv.slice(2),before=path.resolve(oldArg),after=path.resolve(newArg),base=path.join(after,'evidence');
function rows(root){return fs.readFileSync(path.join(root,'evidence/asset-exports.jsonl'),'utf8').trim().split(/\r?\n/).map(JSON.parse).filter(r=>r.type==='AnimationClip'&&path.basename(r.source)==='300100014000_0.db');}
const oldRows=new Map(rows(before).filter(r=>r.ok).map(r=>[r.pathId+'|'+r.format,r])),newRows=rows(after),proof=JSON.parse(fs.readFileSync(path.join(base,'QUATERNION_FIXED_REVIEW.json')));
const expectedChanged=new Set(proof.records.filter(r=>r.parts.some(p=>p.packedQuaternion)).map(r=>r.pathId));
async function sha(file){const h=crypto.createHash('sha256');for await(const b of fs.createReadStream(file))h.update(b);return h.digest('hex');}
(async()=>{
 assert.equal(newRows.length,62*3);const records=[],verified=[];
 for(const row of newRows){assert(row.ok&&!row.rawFallback,JSON.stringify(row));assert.equal(await sha(row.path),row.sha256);assert(fs.statSync(row.path).size>0);verified.push({pathId:row.pathId,format:row.format,path:row.path,sha256:row.sha256});}
 for(const row of newRows.filter(r=>r.format==='curves-json')){
  const old=oldRows.get(row.pathId+'|curves-json');assert(old);assert.equal(await sha(old.path),old.sha256);
  const a=JSON.parse(fs.readFileSync(old.path)),b=JSON.parse(fs.readFileSync(row.path));
  for(const field of ['name','duration','sampleRate','objectReferenceCurves','mode'])assert.deepEqual(b[field],a[field],field+' changed: '+row.name);
  assert.deepEqual(b.tracks.filter(t=>t.property!=='rotation'),a.tracks.filter(t=>t.property!=='rotation'),'Non-rotation curves changed: '+row.name);
  const ar=a.tracks.filter(t=>t.property==='rotation'),br=b.tracks.filter(t=>t.property==='rotation');
  assert.deepEqual(br.map(t=>[t.path,t.property,t.classId,t.keys.map(k=>k.time)]),ar.map(t=>[t.path,t.property,t.classId,t.keys.map(k=>k.time)]),'Rotation binding/timing changed: '+row.name);
  const changed=JSON.stringify(ar)!==JSON.stringify(br);assert.equal(changed,expectedChanged.has(row.pathId),'Unexpected changed clip: '+row.name);
  records.push({name:row.name,pathId:row.pathId,rotationValuesChanged:changed,nonRotationCurvesUnchanged:true,bindingsAndTimesUnchanged:true});
 }
 const result={passed:true,clips:records.length,changed:records.filter(r=>r.rotationValuesChanged).length,unchanged:records.filter(r=>!r.rotationValuesChanged).length,verifiedExports:verified.length,baselineRoot:before,records,files:verified,scope:'Current project exports versus the supplied baseline export context. Only rotation key values change for the 35 proven packed clips; no runtime VFX/game validation claimed.'};
 fs.writeFileSync(path.join(base,'QUATERNION_EXPORT_REGRESSION.json'),JSON.stringify(result,null,2));console.log(JSON.stringify({...result,records:undefined,files:undefined}));
})().catch(e=>{console.error(e);process.exitCode=1;});
