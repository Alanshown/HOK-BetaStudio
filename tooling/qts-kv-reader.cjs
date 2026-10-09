// Bounded generic QTS key/value records, without assuming an outer VFS codec.
module.exports=function read(b){
 function range(p,n){if(!Number.isSafeInteger(p)||!Number.isSafeInteger(n)||p<0||n<0||p>b.length-n)throw Error('KV range '+p+'+'+n);}
 range(0,80);if(b.subarray(0,8).toString('hex')!=='0100000201020304')throw Error('KV magic');
 const tables=new Set([48,56,64].map(p=>b.readInt32LE(p))),queue=[],pages=new Set(),records=new Set();
 for(const t of tables){if(t===-1)continue;range(t,24);const n=b.readInt32LE(t+4),capacity=b.readInt32LE(t);if(n<0||n>capacity)throw Error('KV table count');range(t+24,n*24);for(let i=0;i<n;i++)for(let j=0;j<3;j++){const p=b.readInt32LE(t+24+i*24+j*4);if(p>0)queue.push(p);}}
 for(let at=0;at<queue.length;at++){const p=queue[at];if(pages.has(p))continue;pages.add(p);range(p,4096);const n=b.readUInt16LE(p+4092),type=b.readUInt16LE(p+4094),next=b.readInt32LE(p+4088);if(n>510||![0,2,3].includes(type))throw Error('KV page type/count');if(next>0)queue.push(next);for(let i=0;i<n;i++){const x=b.readInt32LE(p+2040+i*4);if(x>0){if(type===2)queue.push(x);else records.add(x);}}}
 const pending=[...records],visited=new Set(),out=[];
 for(let i=0;i<pending.length;i++){const p=pending[i];if(visited.has(p))continue;visited.add(p);range(p,28);if(b.readInt32LE(p)!==p)throw Error('KV self offset');const next=b.readInt32LE(p+4);if(next!==-1)pending.push(next);const bytes=b.readInt32LE(p+8),keys=b.readInt32LE(p+20),values=b.readInt32LE(p+24);if(keys<1||keys>4096||values<0)throw Error('KV key/value sizes');const keyAt=(p+28+values+3)&~3;range(p,bytes);range(keyAt,keys);if(keyAt+keys>p+bytes)throw Error('KV record size');out.push({offset:p,next,recordBytes:bytes,valueOffset:p+28,value:b.subarray(p+28,p+28+values),keyOffset:keyAt,key:b.subarray(keyAt,keyAt+keys)});}
 return out;
};
