const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict'),{pathToFileURL}=require('node:url');
(async()=>{
 const root=path.resolve(__dirname,'..'),dir=path.resolve(process.argv[2]),base=path.join(root,'frontend/node_modules/three');
 const THREE=await import(pathToFileURL(path.join(base,'build/three.module.js'))),{FBXLoader}=await import(pathToFileURL(path.join(base,'examples/jsm/loaders/FBXLoader.js')));global.window={URL};
 const checks=[];
 for(const [name,takes,bones]of [['standalone',0,0],['rigged',2,1],['static',0,1],['clip',1,1],['joint-mesh',2,1],['override',1,1],['instanced-selected',2,1]]){
  const bytes=fs.readFileSync(path.join(dir,name+'.fbx')),scene=new FBXLoader().parse(bytes.buffer.slice(bytes.byteOffset,bytes.byteOffset+bytes.byteLength),'');
  let count=0,joints=0;scene.traverse(o=>{if(o.isMesh){count++;assert.equal(o.geometry.attributes.position.count,3);if(o.isSkinnedMesh)joints+=o.skeleton.bones.length;}});assert.equal(count,1);assert.equal(joints,bones);assert.equal(scene.animations.length,takes);
  if(takes){const animation=scene.animations.find(a=>a.name===(name==='override'?'Other':'Move'));assert(animation&&animation.tracks.length>0);const mixer=new THREE.AnimationMixer(scene);mixer.clipAction(animation).setLoop(THREE.LoopOnce).play();mixer.setTime(.5);assert(Math.abs(scene.getObjectByName('Rig').position.x+(name==='override'?2.5:1))<1e-5,'Root animation must play with exact expected motion (right-handed conversion)');mixer.stopAllAction();}
  checks.push({name,meshes:count,bones:joints,animationTakes:scene.animations.length});
 }
 const modernBytes=fs.readFileSync(path.join(dir,'modern-root.fbx')),modern=new FBXLoader().parse(modernBytes.buffer.slice(modernBytes.byteOffset,modernBytes.byteOffset+modernBytes.byteLength),'');
 assert.equal(modern.animations.length,1);const modernMixer=new THREE.AnimationMixer(modern);modernMixer.clipAction(modern.animations[0]).play();modernMixer.setTime(.5);
 assert(Math.abs(modern.getObjectByName('Joint').position.x+2)<1e-5);assert.equal(modern.getObjectByName('Rig').position.x,0);modernMixer.stopAllAction();
 checks.push({name:'modern-root',animationTakes:1,childRootBinding:true});
 const variantBytes=fs.readFileSync(path.join(dir,'variant-clip.fbx')),variant=new FBXLoader().parse(variantBytes.buffer.slice(variantBytes.byteOffset,variantBytes.byteOffset+variantBytes.byteLength),'');
 assert.equal(variant.animations.length,1);assert(!variant.getObjectByName('StrippedVariant'));
 const variantMixer=new THREE.AnimationMixer(variant);variantMixer.clipAction(variant.animations[0]).play();variantMixer.setTime(.5);
 assert(Math.abs(variant.getObjectByName('Joint').position.x+1)<1e-5);assert.equal(variant.getObjectByName('Rig').position.x,0);variantMixer.stopAllAction();
 checks.push({name:'variant-clip',animationTakes:1,selectedCompatibleHierarchy:true});
 fs.writeFileSync(path.join(dir,'reimport.json'),JSON.stringify({passed:true,checks},null,2));console.log(JSON.stringify({passed:true,checks}));
})().catch(e=>{console.error(e);process.exitCode=1});
