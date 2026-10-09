// One DB per invocation. Re-import actual OBJ/FBX files with independent Three.js readers.
const fs=require('node:fs'),path=require('node:path'),cp=require('node:child_process'),rl=require('node:readline'),assert=require('node:assert/strict'),crypto=require('node:crypto'),{pathToFileURL}=require('node:url');
const root=path.resolve(__dirname,'..'),[buildArg,sourceArg,outArg]=process.argv.slice(2),build=path.resolve(buildArg),source=path.resolve(sourceArg),out=path.resolve(outArg);
const hash=async f=>{const h=crypto.createHash('sha256');for await(const b of fs.createReadStream(f))h.update(b);return h.digest('hex')};
async function readers(){
 const base=path.join(root,'frontend/node_modules/three');
 const three=await import(pathToFileURL(path.join(base,'build/three.module.js')));
 global.window={URL};
 // Do not perform network/image loading in the geometry checker; retain real texture paths.
 three.TextureLoader.prototype.load=function(url){const t=new three.Texture();t.name=url;return t};
 const {FBXLoader}=await import(pathToFileURL(path.join(base,'examples/jsm/loaders/FBXLoader.js')));
 const {OBJLoader}=await import(pathToFileURL(path.join(base,'examples/jsm/loaders/OBJLoader.js')));
 return {three,FBXLoader,OBJLoader};
}
function geometry(scene){let meshes=0,triangles=0,bones=0;scene.traverse(o=>{if(!o.isMesh)return;meshes++;const g=o.geometry,p=g.getAttribute('position');assert(p&&p.count>0,'Empty geometry');for(const x of p.array)assert(Number.isFinite(x),'Non-finite position');if(g.index)for(const i of g.index.array)assert(i>=0&&i<p.count,'Invalid index');triangles+=(g.index?.count??p.count)/3;
 for(const name of ['normal','uv','color']){const a=g.getAttribute(name);if(a)for(const x of a.array)assert(Number.isFinite(x),'Invalid '+name);}
 if(o.isSkinnedMesh){assert(o.skeleton.bones.length>0);bones+=o.skeleton.bones.length;const si=g.getAttribute('skinIndex'),sw=g.getAttribute('skinWeight');assert(si&&sw);for(let i=0;i<si.array.length;i++)if(sw.array[i]>0)assert(si.array[i]>=0&&si.array[i]<o.skeleton.bones.length,'Invalid skin bone index');}
 });return{meshes,triangles,bones};}
function triangleHash(scene){const triangles=[];scene.traverse(o=>{if(!o.isMesh)return;const g=o.geometry,p=g.getAttribute('position'),count=g.index?.count??p.count;for(let i=0;i<count;i+=3){const points=[];for(let j=0;j<3;j++){const v=g.index?g.index.getX(i+j):i+j;points.push([p.getX(v),p.getY(v),p.getZ(v)].map(x=>x.toFixed(5)).join(','));}triangles.push([0,1,2].map(j=>[points[j],points[(j+1)%3],points[(j+2)%3]].join(';')).sort()[0]);}});return crypto.createHash('sha256').update(triangles.sort().join('\n')).digest('hex');}
async function main(){
 assert(fs.statSync(source).isFile(),'Pass exactly one DB file');fs.mkdirSync(out,{recursive:true});const before=await hash(source),{FBXLoader,OBJLoader}=await readers();
 const worker=cp.spawn(path.join(build,'worker/Hok.Worker.exe'),[path.join(out,'cache')],{cwd:path.join(build,'worker'),windowsHide:true});worker.stderr.pipe(fs.createWriteStream(path.join(out,'worker.log')));
 let serial=0;const pending=new Map();rl.createInterface({input:worker.stdout}).on('line',s=>{const r=JSON.parse(s),p=pending.get(r.id);if(p){clearTimeout(p.timer);pending.delete(r.id);r.ok?p.resolve(r.data):p.reject(Error(r.error.message))}});
 const rpc=(method,payload)=>new Promise((resolve,reject)=>{const id=String(++serial),timer=setTimeout(()=>reject(Error('Worker timeout: '+method)),180000);pending.set(id,{resolve,reject,timer});worker.stdin.write(JSON.stringify({id,method,payload})+'\n')});
 const report={source,build,checks:[],errors:[]};
 try{
  report.load=await rpc('load',{paths:[source]});let rows=[];for(let page=0;;page++){const list=await rpc('list',{type:'Mesh',page});rows.push(...list.items);if(rows.length>=list.total)break;}assert(rows.length>0,'Sample has no Mesh');
  for(const row of rows){
   try{
    assert(row.formats.includes('obj')&&row.formats.includes('fbx'),'Missing OBJ / FBX formats');
    const files={};for(const format of ['obj','fbx']){const result=await rpc('export',{assetIds:[row.id],format,output:path.join(out,'导出模型 Tiếng Việt')});assert.equal(result.failed,0,JSON.stringify(result));assert.equal(result.rawFallback,0);files[format]=result.results[0].path;}
    const objText=fs.readFileSync(files.obj,'utf8'),objScene=new OBJLoader().parse(objText),obj=geometry(objScene);
    const data=fs.readFileSync(files.fbx),scene=new FBXLoader().parse(data.buffer.slice(data.byteOffset,data.byteOffset+data.byteLength),path.dirname(files.fbx)+'/'),fbx=geometry(scene),meta=JSON.parse(fs.readFileSync(files.fbx+'.export.json','utf8'));
    assert.equal(obj.triangles,meta.triangles,'OBJ/FBX topology differs');assert.equal(fbx.triangles,meta.triangles,'FBX triangle count changed');assert.equal(objText.split(/\r?\n/).filter(l=>l.startsWith('v ')).length,meta.vertices,'Vertex count changed');assert.equal(scene.animations.length,meta.animations.length);
    assert.equal(triangleHash(objScene),triangleHash(scene),'OBJ/FBX vertex positions or face winding differ');
    if(meta.bones>0)assert(fbx.bones>0,'FBX lost skinning');
    report.checks.push({name:row.name,obj,fbx,animations:scene.animations.length,warnings:meta.warnings,files});console.log(JSON.stringify(report.checks.at(-1)));
   }catch(e){report.errors.push({name:row.name,error:e.stack});console.error(row.name,e.message);}
   await rpc('releasePreview',{});
  }
  report.originalUnchanged=before===await hash(source);assert(report.originalUnchanged);assert.deepEqual(report.errors,[]);
 }finally{worker.kill();for(const p of pending.values())clearTimeout(p.timer);fs.writeFileSync(path.join(out,'report.json'),JSON.stringify(report,null,2));}
 console.log('PASS',report.checks.length,'meshes exported as real OBJ and FBX, re-imported, original DB unchanged');
}
main().catch(e=>{console.error(e);process.exitCode=1});
