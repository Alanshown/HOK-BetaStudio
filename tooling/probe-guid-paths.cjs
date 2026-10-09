const fs=require('node:fs'),path=require('node:path');
const root=path.resolve(__dirname,'..'),r=JSON.parse(fs.readFileSync(path.join(root,'planning/mapping-audit-report.json')));
const ids=new Set(r.entries.map(e=>e.id));
function hash(s){let a=0x5bd1e995,b=0xab9423a7;s=s.toLowerCase();for(let i=0;i<s.length;i++){a=(Math.imul(a,33)^s.charCodeAt(i))>>>0;b=(Math.imul(b,33)^s.charCodeAt(s.length-1-i))>>>0;}return((BigInt(b)<<32n)|BigInt(a)).toString()}
const matches=[],seen=new Set();let attempted=0;
for(const f of r.files)for(const e of f.externals){
 if(seen.has(e.guid))continue;seen.add(e.guid);
 const g=e.guid.replaceAll('-',''),raw=g.slice(6,8)+g.slice(4,6)+g.slice(2,4)+g.slice(0,2)+g.slice(10,12)+g.slice(8,10)+g.slice(14,16)+g.slice(12,14)+g.slice(16);
 const variants=new Set();
 for(const v of [g,raw])for(const width of [2,4,8,32]){
  variants.add(v.match(new RegExp('.{'+width+'}','g')).map(s=>[...s].reverse().join('')).join(''));
  variants.add(v.match(new RegExp('.{'+width+'}','g')).map(s=>s.match(/../g).reverse().join('')).join(''));
  variants.add(v);
 }
 for(const v of variants)for(const prefix of ['', '/', 'assets/', '/assets/', 'library/', '/library/'])for(const folder of ['',v.slice(0,2)+'/',v.slice(-2)+'/'])for(const suffix of ['', '.assets','.asset','.prefab','.bundle','.unity3d','.resS']){
  const candidate=prefix+folder+v+suffix,id=hash(candidate);attempted++;
  if(ids.has(id))matches.push({guid:e.guid,candidate,id});
 }
}
const report={date:new Date().toISOString(),attempted,matches,scope:'Bounded path-hash hypothesis test only; not an implemented or verified general GUID resolver.'};
fs.writeFileSync(path.join(root,'planning/guid-path-probe.json'),JSON.stringify(report,null,2));console.log(JSON.stringify(report));
