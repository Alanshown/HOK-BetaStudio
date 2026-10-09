const fs=require('node:fs'),path=require('node:path'),cp=require('node:child_process'),crypto=require('node:crypto'),assert=require('node:assert/strict');
const root=fs.mkdtempSync(path.resolve('.cache/promote-graph-')),base=path.join(root,'evidence');fs.mkdirSync(path.join(base,'db'),{recursive:true});fs.mkdirSync(path.join(base,'probes'));
const json=(f,x)=>fs.writeFileSync(f,JSON.stringify(x)),source=path.join(root,'source.db'),scan=path.join(base,'source.jsonl'),key=crypto.createHash('sha256').update(source.toLowerCase()).digest('hex').slice(0,24);
json(path.join(base,'inventory.json'),{files:[source]});json(path.join(base,'db',key+'.report.json'),{output:scan,indexed:{sourceSha256:'a'.repeat(64)}});
const main=path.join(base,'dependency-graph.json'),supplemental=path.join(base,'additional-dependency-graph.json'),candidate=path.join(base,'probes','new.json');
const nodes=['old','additional'].map(asset_id=>({sourcePath:source,asset_id}));json(main,{nodes:[nodes[0]]});json(supplemental,{nodes:[nodes[1]]});const previous=fs.readFileSync(main,'utf8');
const graph={nodes,indexedSources:1,semanticComplete:false,sources:[{path:source,evidence:scan,sha256:'a'.repeat(64),complete:1}]};
function run(value){json(candidate,value);return cp.spawnSync(process.execPath,[path.join(__dirname,'promote-evidence-graph.cjs'),root,candidate],{encoding:'utf8'});}
assert.notEqual(run({...graph,nodes:[nodes[0]]}).status,0);assert.equal(fs.readFileSync(main,'utf8'),previous);assert(fs.existsSync(supplemental));
assert.notEqual(run({...graph,sources:[{...graph.sources[0],evidence:'stale.jsonl'}]}).status,0);assert.equal(fs.readFileSync(main,'utf8'),previous);
const result=run(graph);assert.equal(result.status,0,result.stderr);const report=JSON.parse(result.stdout);assert.equal(JSON.parse(fs.readFileSync(main)).nodes.length,2);assert(!fs.existsSync(supplemental));assert.equal(report.backups.length,2);assert.equal(fs.readFileSync(report.backups.find(x=>x.file===main).saved,'utf8'),previous);assert(report.backups.every(x=>fs.existsSync(x.saved)));
console.log(JSON.stringify({passed:5,checks:['lost selections rejected before changes','stale scans rejected before changes','fresh complete graph promoted','supplemental graph preserved in history','previous main is recoverable']}));
