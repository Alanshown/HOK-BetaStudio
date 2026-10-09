const crypto=require('node:crypto');
// Format/hash cross-check: bnnm/wwiser, wwiser/wfnv.py and parser/wparser.py.
// This deliberately reads only version-135 HIRC record identities, not sound
// routing semantics. A matching event hash is not a complete playback graph.
function eventId(name){let value=2166136261;for(const byte of Buffer.from(name.toLowerCase(),'utf8'))value=(Math.imul(value,16777619)^byte)>>>0;return String(value);}
function inspect(bytes){
 function need(at,n,end=bytes.length){if(at<0||n<0||at>end-n)throw Error('SoundBank range at '+at);}
 function u32(at,end){need(at,4,end);return bytes.readUInt32LE(at);}
 const chunks=[],objects=[],duplicateIdentities=[];let at=0,version=null,bankId=null,seenHierarchy=false;
 while(at<bytes.length){need(at,8);const tag=bytes.toString('ascii',at,at+4),size=u32(at+4),start=at+8,end=start+size;need(start,size);
  if(at===0&&tag!=='BKHD')throw Error('SoundBank must start with BKHD');
  if(tag==='BKHD'){if(version!==null)throw Error('Duplicate BKHD');version=u32(start,end);bankId=String(u32(start+4,end));if(version!==135)throw Error('HIRC identity layout has not been validated for version '+version);}
  if(tag==='HIRC'){
   if(seenHierarchy)throw Error('Duplicate HIRC');seenHierarchy=true;const count=u32(start,end);let p=start+4;
   if(count>Math.floor((end-p)/9))throw Error('HIRC count exceeds chunk');const ids=new Map();
   for(let i=0;i<count;i++){need(p,9,end);const type=bytes[p],length=u32(p+1,end);if(length<4)throw Error('HIRC object lacks ID');need(p+5,length,end);
    const id=String(u32(p+5,end)),identity=type+':'+id;
    // IDs belong to Wwise object domains, not one global integer namespace.
    // Even conflicting same-type records must remain visible, not cause all
    // otherwise valid events in this bank to disappear from the index.
    if(ids.has(identity))duplicateIdentities.push({type,id,offsets:[ids.get(identity),p],status:'ambiguous-same-type-records'});else ids.set(identity,p);
    objects.push({type,id,key:identity+':'+p,offset:p,bytes:length+5,payloadOffset:p+9,payloadBytes:length-4,
     sha256:crypto.createHash('sha256').update(bytes.subarray(p,p+5+length)).digest('hex'),semanticStatus:'identity-and-range-only'});p+=5+length;
   }
   if(p!==end)throw Error('HIRC undeclared trailing bytes');
  }
  chunks.push({tag,offset:at,bytes:size});at=end;
 }
 if(version===null)throw Error('No BKHD');return{version,bankId,chunks,objects,events:objects.filter(o=>o.type===4),duplicateIdentities,semanticComplete:false};
}
module.exports={eventId,inspect};
