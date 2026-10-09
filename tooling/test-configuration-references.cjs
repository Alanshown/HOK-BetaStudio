const assert=require('node:assert/strict'),{xmlReferences}=require('./configuration-references.cjs');
const track={guid:'t',enabled:'false',name:'conditional sound',eventType:'PlayHeroSoundTick',conditions:[{guid:'skin-selector',status:'false'}],skinFilters:['14006'],skinOrAvatarIds:[]};
const rows=xmlReferences({tracks:[track],references:[
 {field:'eventName',eventName:'PlayHeroSoundTick',path:'Play_GuanYu_M_Skill_C',trackGuid:'t',byteOffset:80,byteCount:21},
 {field:'resourceName',eventName:'TriggerParticle',path:'Prefab/Effect/14006',trackGuid:'t'},
 {field:'text',eventName:'OtherEvent',path:'not-a-resource'},
 {field:'text',eventName:'OtherEvent',path:'https://example.test/a'}]});
assert.equal(rows.length,2);assert.equal(rows[0].referenceKind,'audio-event-name');assert.equal(rows[0].path,'Play_GuanYu_M_Skill_C');assert.equal(rows[0].byteOffset,80);
assert.equal(rows[0].trackContext.enabled,'false');assert.deepEqual(rows[0].trackContext.conditions,track.conditions);assert.deepEqual(rows[0].trackContext.skinFilters,['14006']);
assert.equal(rows[0].runtimeApplicability,'conditional-not-evaluated');assert.equal(rows[1].referenceKind,'resource-path');
console.log(JSON.stringify({passed:9,scope:'Symbolic sound calls survive discovery without inventing bank/path identities or evaluating conditions'}));
