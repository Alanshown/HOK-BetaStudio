const fs=require('node:fs'),path=require('node:path'),read=require('./qts-kv-reader.cjs');
const [file,hash='14B8A76D7AC245084ABCE149618CFB17']=process.argv.slice(2);
if(!file)throw Error('Usage: node inspect-qts-type-trees.cjs <TTre payload> [type hash]');
const common=new Map([...fs.readFileSync(path.join(__dirname,'../vendor/Studio-HoK/AssetStudio/CommonString.cs'),'utf8').matchAll(/\{(\d+),\s*"([^"]*)"\}/g)].map(m=>[+m[1],m[2]]));
function parse(b){
 if(b.length<8)throw Error('header');const n=b.readInt32LE(),s=b.readInt32LE(4);if(n<1||n>100000||s<0||8+n*32+s!==b.length)throw Error('layout');
 const strings=b.subarray(8+n*32),text=o=>{if(o>>>31){const v=common.get(o&0x7fffffff);if(v===undefined)throw Error('common string '+o);return v;}const end=strings.indexOf(0,o);if(o>=strings.length||end<0)throw Error('string offset');return strings.toString('utf8',o,end);};
 return Array.from({length:n},(_,i)=>{const p=8+i*32;return {level:b[p+2],type:text(b.readUInt32LE(p+4)),name:text(b.readUInt32LE(p+8)),bytes:b.readInt32LE(p+12),index:b.readInt32LE(p+16),flags:b.readInt32LE(p+20),version:b.readUInt16LE(p),refTypeHash:b.readBigUInt64LE(p+24).toString()};});
}
const records=read(fs.readFileSync(file)),errors=[],types={},selected=[];
for(const r of records){try{const nodes=parse(r.value);types[nodes[0].type]=(types[nodes[0].type]||0)+1;if(r.key.toString('hex').toUpperCase()===hash.toUpperCase())selected.push({key:r.key.toString('hex'),offset:r.offset,nodes});}catch(e){errors.push({offset:r.offset,error:e.message});}}
console.log(JSON.stringify({records:records.length,errors,types,selected},null,2));
