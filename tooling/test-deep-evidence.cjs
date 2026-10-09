const fs=require('node:fs'),path=require('node:path'),cp=require('node:child_process'),assert=require('node:assert/strict');
const root=fs.mkdtempSync(path.resolve('.cache/deep-evidence-test-')),before=path.join(root,'before.jsonl'),after=path.join(root,'after.jsonl'),report=path.join(root,'check.json');
const source={kind:'source',source:'fixture.db',sha256:'a'.repeat(64),deep:false};
const object={kind:'object',id:'sf:9007199254740999',sha256:'b'.repeat(64),type:'Mesh',parseStatus:'deferred',remaining:100,issues:[]};
function write(file,deep,obj){fs.writeFileSync(file,[{...source,deep},obj,{kind:'summary',objects:1,references:deep?2:0}].map(JSON.stringify).join('\n')+'\n');}
write(before,false,object);
function run(deep,override={},ok=true){write(after,deep,{...object,parseStatus:'typed-complete',remaining:0,...override});const p=cp.spawnSync(process.execPath,[path.join(__dirname,'validate-deep-evidence.cjs'),before,after,report],{encoding:'utf8'});assert.equal(p.status===0,ok,p.stdout+p.stderr);return ok?JSON.parse(fs.readFileSync(report)):null;}
assert.equal(run(true).allObjectsFullyRead,true);
run(false,{},false);run(true,{sha256:'c'.repeat(64)},false);run(true,{id:'sf:9007199254740998'},false);
assert.equal(run(true,{parseStatus:'typed-partial',remaining:10}).allObjectsFullyRead,false);
assert.equal(run(true,{issues:['Incomplete reference read']}).allObjectsFullyRead,false);
write(before,false,{...object,sha256:null});run(true,{},false);
console.log(JSON.stringify({passed:7,checks:['deep mode required','exact original hashes','64-bit object identity','partial reads retained','diagnostics prevent full-read claim','missing hashes rejected','full reads do not assert runtime closure']}));
