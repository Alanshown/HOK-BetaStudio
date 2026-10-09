const fs=require('node:fs'),path=require('node:path'),cp=require('node:child_process'),assert=require('node:assert/strict');
const root=fs.mkdtempSync(path.resolve('.cache/export-plan-')),base=path.join(root,'evidence');fs.mkdirSync(base);
const defaultGraph=path.join(base,'dependency-graph.json'),alternative=path.join(base,'expanded.json');
fs.writeFileSync(defaultGraph,JSON.stringify({nodes:[]}));
fs.writeFileSync(alternative,JSON.stringify({nodes:[{sourcePath:'a.db',asset_id:'attempted'},{sourcePath:'a.db',asset_id:'new:9007199254740993'},{sourcePath:'b.db',asset_id:'attempted'}]}));
fs.writeFileSync(path.join(base,'asset-exports.jsonl'),JSON.stringify({source:'a.db',id:'attempted',ok:false})+'\n');
const run=cp.spawnSync(process.execPath,[path.join(__dirname,'export-unattempted-evidence.cjs'),'nonexistent-build',root,'nonexistent-schema'],{encoding:'utf8',env:{...process.env,HOK_EVIDENCE_GRAPH:alternative,HOK_EVIDENCE_EXPORT_PLAN:'1'}});
assert.equal(run.status,0,run.stderr);const result=JSON.parse(run.stdout);assert.equal(result.graph,alternative);assert.equal(result.readOnlyPlan,true);assert.equal(result.newObjects,2);assert.deepEqual(result.sources,[{source:'a.db',ids:['new:9007199254740993']},{source:'b.db',ids:['attempted']}]);assert.equal(fs.readFileSync(defaultGraph,'utf8'),'{"nodes":[]}');
console.log(JSON.stringify({passed:5,checks:['alternate graph honored','plan starts no worker','64-bit IDs retained','same ID in another source retained','prior failures are not disguised as unattempted objects']}));
