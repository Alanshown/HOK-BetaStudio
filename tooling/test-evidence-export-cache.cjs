const assert=require('node:assert/strict'),{context,reusable}=require('./evidence-export-cache.cjs');
const input={parserRevision:'parser',sourceSha256:'ABC123',schemaSha256:'schema',dependencies:[{path:'b.db',bytes:30,mtimeMs:1},{path:'a.db',bytes:20,mtimeMs:1}],selectedEntries:['20','10']};
const key=context(input),saved={ok:true,exportContext:key};let passed=0;
function check(value){assert(value);passed++;}
check(reusable(saved,key));
check(context({...input,dependencies:[...input.dependencies].reverse(),selectedEntries:['10','20','10']})===key);
check(context({...input,sourceSha256:'abc123'})===key);
for(const change of [{sourceSha256:'different-db'},{parserRevision:'new-parser'},{schemaSha256:'new-schema'},
 {dependencies:[...input.dependencies,{path:'new.db',bytes:1,mtimeMs:1}]},
 {dependencies:input.dependencies.map(x=>({...x,mtimeMs:2}))},
 {dependencies:input.dependencies.map(x=>({...x,bytes:x.bytes+1}))},
 {selectedEntries:['10','20','30']},{selectedEntries:null}])check(!reusable(saved,context({...input,...change})));
check(!reusable({ok:true},key));
check(!reusable({...saved,rawFallback:true},key));
check(!reusable({...saved,ok:false},key));
console.log(JSON.stringify({passed,scope:'Export cache identity; does not attest external dependency semantics'}));
