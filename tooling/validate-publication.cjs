const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const root=path.resolve(__dirname,'..');
async function main(){
 const reads=['README.md','README.zh.md','README.vi.md'].map(p=>fs.readFileSync(path.join(root,p),'utf8'));
 const code=s=>Array.from(s.matchAll(/```[^\n]*\n([\s\S]*?)```/g),m=>m[1]);
 for(const [i,s]of reads.entries()){
  assert.equal((s.match(/README-I18N:START/g)||[]).length,1);assert.equal((s.match(/README-I18N:END/g)||[]).length,1);
  assert.deepEqual(code(s),code(reads[0]));
  for(const m of s.matchAll(/\]\(#([^)]*)\)/g))assert(s.includes('id="'+m[1]+'"'),m[1]);
  for(const m of s.matchAll(/(?:\]\(|src=")([^\s)"#]+)(?:\)|")/g)){if(!/^(https?:|#)/.test(m[1]))assert(fs.existsSync(path.join(root,m[1])),m[1])}
  assert(!/[CD]:[\\/]Users|Nox_share/.test(s),'Private path in README '+i);
 }
 console.log(JSON.stringify({readmeLanguages:3,matchingCodeBlocks:true,anchorsAndFiles:true}));
}
main().catch(e=>{console.error(e);process.exitCode=1});
