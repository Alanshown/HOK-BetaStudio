const assert=require('node:assert/strict'),{inspect,eventId}=require('./wwise-bank-evidence.cjs');
function chunk(name,bytes){const h=Buffer.alloc(8);h.write(name,0,'ascii');h.writeUInt32LE(bytes.length,4);return Buffer.concat([h,bytes]);}
const h=Buffer.alloc(8);h.writeUInt32LE(135);h.writeUInt32LE(777,4);const object=Buffer.alloc(10);object[0]=4;object.writeUInt32LE(5,1);object.writeUInt32LE(Number(eventId('Play_GuanYu_M_Skill_C')),5);object[9]=0;
const count=Buffer.alloc(4);count.writeUInt32LE(1);const raw=Buffer.concat([chunk('BKHD',h),chunk('HIRC',Buffer.concat([count,object]))]);let passed=0;
function check(value){assert(value);passed++;}function reject(bytes){assert.throws(()=>inspect(bytes));passed++;}
check(eventId('')==='2166136261');check(eventId('a')==='84696446');check(eventId('PLAY_GUANYU')===eventId('play_guanyu'));
const bank=inspect(raw);check(bank.bankId==='777');check(bank.events.length===1&&bank.events[0].id===eventId('Play_GuanYu_M_Skill_C'));check(bank.events[0].payloadBytes===1);check(bank.semanticComplete===false);
reject(raw.subarray(0,raw.length-1));let bad=Buffer.from(raw);bad.writeUInt32LE(2,24);reject(bad);bad=Buffer.from(raw);bad.writeUInt32LE(99,29);reject(bad);bad=Buffer.from(raw);bad.writeUInt32LE(134,8);reject(bad);
const second=Buffer.from(object);second[0]=18;count.writeUInt32LE(2);
const crossType=inspect(Buffer.concat([chunk('BKHD',h),chunk('HIRC',Buffer.concat([count,object,second]))]));
check(crossType.objects.length===2&&crossType.events.length===1&&crossType.duplicateIdentities.length===0);
const conflict=inspect(Buffer.concat([chunk('BKHD',h),chunk('HIRC',Buffer.concat([count,object,object]))]));
check(conflict.events.length===2&&conflict.duplicateIdentities.length===1&&new Set(conflict.objects.map(o=>o.key)).size===2);
console.log(JSON.stringify({passed,scope:'Version-135 bank identities, strict HIRC/chunk bounds and Wwise name hashing'}));
