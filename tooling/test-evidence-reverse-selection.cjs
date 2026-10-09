// ScriptableObject/custom component clip references must survive selection.
// A reverse PathID match stays a candidate, never an invented model binding.
const fs=require('node:fs'),path=require('node:path'),cp=require('node:child_process'),assert=require('node:assert/strict'),{DatabaseSync}=require('node:sqlite');
const scratch=fs.mkdtempSync(path.resolve('.cache/reverse-selection-')),base=path.join(scratch,'evidence');
fs.mkdirSync(path.join(base,'db'),{recursive:true});fs.mkdirSync(path.join(base,'candidates'));
fs.writeFileSync(path.join(base,'inventory.json'),JSON.stringify({total:1}));
const file=path.join(scratch,'fixture.sqlite');
const init=cp.spawnSync(process.execPath,['--no-warnings',path.join(__dirname,'build-evidence-db.cjs'),scratch,file],{encoding:'utf8',windowsHide:true});assert.equal(init.status,0,init.stderr);
const db=new DatabaseSync(file),source=path.join(scratch,'fixture.db');
db.prepare('INSERT INTO sources(id,path,sha256,evidence,complete) VALUES(1,?,?,?,1)').run(source,'source-sha',path.join(base,'source.jsonl'));
const insert=db.prepare('INSERT INTO objects(id,source,asset_id,sf,entry,sf_offset,path_id,class_id,type,name,status,remaining) VALUES(?,1,?,?,?,0,?,?,?, ?,\'typed-complete\',0)');
insert.run(1,'sf:9007199254740993','animation-sf','10','9007199254740993',74,'AnimationClip','Exclusive14006_01');
insert.run(2,'sf:7','script-sf','11','7',114,'MonoBehaviour','140_guanyu_extraclips_6');
insert.run(3,'sprite:3','sprite-sf','12','3',1,'GameObject','13_14006');
insert.run(4,'sprite:4','sprite-sf','12','4',212,'SpriteRenderer','');
db.prepare('INSERT INTO refs(owner,field,file_id,path_id,expected_type,status,target_sf) VALUES(2,\'m_extraClips[0]\',1,?,\'AnimationClip\',\'local-candidate\',\'animation-sf\')').run('9007199254740993');
db.prepare('INSERT INTO refs(owner,field,file_id,path_id,expected_type,status,target_sf) VALUES(3,\'m_Components[0]\',0,\'4\',\'Component\',\'confirmed\',\'sprite-sf\')').run();
db.close();
fs.writeFileSync(path.join(base,'candidates/source.json'),JSON.stringify({source,objects:[{id:'sf:9007199254740993',matchKind:'serialized-name-match'},{id:'sprite:3',matchKind:'serialized-name-match'}]}));
const run=cp.spawnSync(process.execPath,['--no-warnings',path.join(__dirname,'trace-evidence-graph.cjs'),scratch,'14006',file],{encoding:'utf8',windowsHide:true,env:{...process.env,HOK_EVIDENCE_GRAPH:path.join(base,'result.json')}});assert.equal(run.status,0,run.stderr);
const graph=JSON.parse(fs.readFileSync(path.join(base,'result.json')));
assert.equal(graph.nodes.length,4);assert(graph.nodes.find(n=>n.type==='MonoBehaviour').reasons.includes('reverse-use-of-named-asset'));
const clipRef=graph.edges.find(e=>e.field==='m_extraClips[0]'),spriteRef=graph.edges.find(e=>e.field==='m_Components[0]');
assert.equal(graph.reverse[0].status,'reverse-PathID-candidate');assert.equal(clipRef.identityConfirmed,false);assert.equal(clipRef.resolvedStatus,'local-candidate');
assert.equal(clipRef.path_id,'9007199254740993');assert(!graph.nodes.some(n=>n.type==='GameObject'&&n.sf==='script-sf'));
assert.equal(spriteRef.resolvedStatus,'confirmed');assert.equal(spriteRef.candidates[0].type,'SpriteRenderer');
console.log(JSON.stringify({passed:7,scratch,checks:['script clip reference selected','reverse candidate retained','GUID identity not invented','64-bit PathID preserved','no model fabricated','separate graph destination','SpriteRenderer is a valid Component reference']}));
