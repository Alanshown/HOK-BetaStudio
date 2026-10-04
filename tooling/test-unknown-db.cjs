// Real DB integration test. All caches/exports are isolated; input hashes are checked.
const {chromium}=require('playwright-core');
const fs=require('node:fs'),path=require('node:path'),cp=require('node:child_process'),readline=require('node:readline'),assert=require('node:assert/strict'),crypto=require('node:crypto');
const root=path.resolve(__dirname,'..'),source=process.argv[2]?path.resolve(process.argv[2]):null,build=path.resolve(root,process.argv[3]||'build/HOK-BetaStudio-1.3-hotfix-win-x64');
assert(source,'Usage: node tooling/test-unknown-db.cjs <unknown DB folder> [build directory]');
const temp=fs.mkdtempSync(path.join(root,'.cache','unknown-db-test-')),pause=ms=>new Promise(r=>setTimeout(r,ms));
const inputs=fs.readdirSync(source).filter(n=>/\.db$/i.test(n)).map(n=>path.join(source,n));
const hash=f=>crypto.createHash('sha256').update(fs.readFileSync(f)).digest('hex'),originals=Object.fromEntries(fs.readdirSync(source).map(n=>[n,hash(path.join(source,n))]));
const results=[],pass=text=>{results.push(text);console.log('PASS '+text)};
async function main(){
 const worker=cp.spawn(path.join(build,'worker','Hok.Worker.exe'),[path.join(temp,'worker-cache')],{cwd:path.join(build,'worker'),windowsHide:true,stdio:['pipe','pipe','pipe']});worker.stderr.resume();
 const pending=new Map();let serial=0,browser,app;
 readline.createInterface({input:worker.stdout}).on('line',line=>{const result=JSON.parse(line),request=pending.get(result.id);if(request){clearTimeout(request.timer);pending.delete(result.id);result.ok?request.resolve(result.data):request.reject(Error(result.error.message))}});
 const rpc=(method,payload)=>new Promise((resolve,reject)=>{const id=String(++serial),timer=setTimeout(()=>{pending.delete(id);reject(Error('Worker timeout: '+method))},120000);pending.set(id,{resolve,reject,timer});worker.stdin.write(JSON.stringify({id,method,payload})+'\n')});
 try{
  const loaded=await rpc('load',{paths:inputs});console.log(JSON.stringify({count:loaded.count,types:loaded.types,parseWarnings:loaded.errors.length}));assert(loaded.count>0);fs.writeFileSync(path.join(temp,'parse-warnings.json'),JSON.stringify(loaded.errors,null,2));pass('Real arbitrarily named DBs load through the normal worker');
  // Filename classification must not affect decoding: compare byte-identical
  // copies using a conventional hero name, including any pre-existing warnings.
  const control=path.join(temp,'numeric-name-control');fs.mkdirSync(control);
  const controlInputs=inputs.map(file=>{const suffix=path.basename(file).match(/(_\d+)?\.db$/i)?.[1]||'';const target=path.join(control,'3200010500'+suffix+'.db');fs.copyFileSync(file,target);return target});
  const baseline=await rpc('load',{paths:controlInputs});assert.equal(baseline.count,loaded.count);assert.deepEqual(baseline.types,loaded.types);assert.equal(baseline.errors.length,loaded.errors.length);pass('Identical bytes with hero-style filenames produce the same assets and parser warnings');
  await rpc('load',{paths:inputs});
  const all=[];for(let p=0;all.length<loaded.count;p++){const r=await rpc('list',{page:p});assert(r.items.length);all.push(...r.items)}
  const exports=path.join(temp,'exports');fs.mkdirSync(exports);
  for(const kind of ['image','model','audio','bank','data']){
   const asset=(kind==='image'?all.find(a=>a.type==='Texture2D'):null)||all.find(a=>a.preview===kind);if(!asset)continue;
   const preview=await rpc(kind==='bank'?'bank':kind==='data'?'dump':'preview',{assetId:asset.id});assert(preview);pass('Preview pipeline: '+kind+' / '+asset.type);
   const format=asset.formats.includes('original')?'original':asset.formats.includes('raw')?'raw':asset.formats[0];
   const report=await rpc('export',{assetIds:[asset.id],format,output:exports,options:{}});assert.equal(report.success,1,JSON.stringify(report));assert.equal(report.failed,0);assert(fs.existsSync(report.results[0].path));pass('Real export: '+asset.type+' / '+format);
   const converted={image:'png',model:'obj',audio:'mp3'}[kind];if(converted&&asset.formats.includes(converted)&&converted!==format){const conversion=await rpc('export',{assetIds:[asset.id],format:converted,output:exports,options:{}});assert.equal(conversion.success,1,JSON.stringify(conversion));assert.equal(conversion.failed,0);pass('Converted export: '+converted)}
  }
  if(loaded.errors.length===0&&loaded.types.Texture2D){
   const asset=all.find(a=>a.type==='ResourceFile');assert(asset);
   const exported=await rpc('export',{assetIds:[asset.id],format:'original',output:exports,options:{}});assert.equal(exported.success,1);
   const original=fs.readFileSync(exported.results[0].path);assert.equal(original.length%16,0);
   const replacement=Buffer.alloc(original.length);for(let i=0;i<replacement.length;i+=16)original.copy(replacement,i,0,16);
   assert.notEqual(hash(exported.results[0].path),crypto.createHash('sha256').update(replacement).digest('hex'));
   const replacementFile=path.join(temp,'replacement.bin');fs.writeFileSync(replacementFile,replacement);
   const staged=await rpc('replace',{assetId:asset.id,path:replacementFile});assert.equal(staged.count,1);
   const rebuilt=await rpc('rebuild',{output:temp});assert(rebuilt.beta);assert.deepEqual(fs.readdirSync(rebuilt.output).sort(),fs.readdirSync(source).sort());
   const reopened=await rpc('load',{paths:fs.readdirSync(rebuilt.output).filter(n=>/\.db$/i.test(n)).map(n=>path.join(rebuilt.output,n))});assert.equal(reopened.count,loaded.count);
   const row=(await rpc('list',{type:'ResourceFile'})).items.find(a=>a.pathId===asset.pathId);assert(row);
   const actual=await rpc('export',{assetIds:[row.id],format:'original',output:exports,options:{}});assert.equal(actual.success,1);assert(fs.readFileSync(actual.results[0].path).equals(replacement));
   pass('Renamed package supports a real Beta replacement, complete rebuild and byte-verified reimport');
  }
  try{await fetch('http://127.0.0.1:9224/json/version');throw Error('Smoke port occupied')}catch(e){if(e.message==='Smoke port occupied')throw e}
  app=cp.spawn(path.join(build,'HOK BetaStudio.exe'),['--smoke','--smoke-data='+path.join(temp,'desktop-data')],{cwd:build,windowsHide:true,stdio:'ignore'});
  for(let i=0;i<100;i++){try{browser=await chromium.connectOverCDP('http://127.0.0.1:9224');break}catch{await pause(200)}}assert(browser);
  let page;for(let i=0;i<100;i++){page=browser.contexts()[0].pages().find(p=>p.url().startsWith('https://hok.local/'));if(page)break;await pause(100)}assert(page);page.setDefaultTimeout(90000);
  const errors=[];page.on('pageerror',e=>errors.push(e.message));await page.waitForFunction(()=>document.querySelectorAll('.hero-card').length>0);await page.locator('.title-right select').selectOption('zh');
  const cdp=await page.context().newCDPSession(page);await cdp.send('Emulation.setDeviceMetricsOverride',{width:1440,height:920,deviceScaleFactor:1,mobile:false});
  async function drop(files){for(const type of ['dragEnter','dragOver','drop'])await cdp.send('Input.dispatchDragEvent',{type,x:500,y:300,data:{items:[],files,dragOperationsMask:1}})}
  await drop([path.resolve(source)]);await page.locator('.unknown-db-row').waitFor();assert.equal(await page.locator('.hero-card,.skin-card').count(),0);assert((await page.locator('.unknown-db-row').innerText()).includes(path.basename(inputs[0])));pass('Unknown-only workspace opens the file list directly, without fake hero/skin IDs');
  await page.locator('.unknown-db-row').click();await page.waitForFunction(n=>document.querySelector('.pagination')?.textContent.startsWith(String(n)),loaded.count);pass('Unknown list loads the complete real asset count: '+loaded.count);
  await page.locator('tbody input').first().check();assert.equal(await page.locator('.replace-button').count(),1);assert(await page.getByRole('button',{name:'导出所选',exact:true}).isEnabled());pass('Same selection, export and Beta replacement controls are available');
  const image=all.find(a=>a.type==='Texture2D');if(image){await page.locator('.asset-toolbar select').selectOption(image.type);await page.waitForFunction(type=>document.querySelector('tbody tr td:nth-child(3)')?.textContent?.startsWith(type),image.type);await page.locator('.asset-name').first().click();await page.locator('.image-preview canvas').waitFor();assert.equal(await page.locator('.preview-navigation button').count(),2);await page.locator('.close-button').click();pass('Actual desktop image preview and previous/next controls work');}
  const audio=all.find(a=>a.preview==='audio');if(audio){await page.locator('.asset-toolbar select').selectOption(audio.type);await page.waitForFunction(type=>document.querySelector('tbody tr td:nth-child(3)')?.textContent?.startsWith(type),audio.type);await page.locator('.asset-name').first().click();await page.waitForFunction(()=>document.querySelector('audio')?.readyState>=1);assert.equal(await page.locator('.preview-navigation button').count(),2);await page.locator('.close-button').click();pass('Actual desktop audio loads into the player with previous/next controls');}
  for(const [lang,label] of [['en','Unknown DB files'],['vi','Tệp DB chưa xác định'],['zh','未知 DB 文件']]){await page.locator('.title-right select').selectOption(lang);assert.equal(await page.locator('.page-head h1').innerText(),label)}pass('Unknown list labels support all three languages');
  await page.screenshot({path:path.join(temp,'unknown-assets-1440.png')});
  await cdp.send('Emulation.setDeviceMetricsOverride',{width:1000,height:800,deviceScaleFactor:1,mobile:false});assert(await page.locator('.workspace').evaluate(e=>e.scrollWidth<=e.clientWidth));pass('Unknown asset view fits the minimum desktop width');
  const known=path.join(root,'3200010500');if(fs.existsSync(known)){await drop([path.resolve(source),known]);await page.locator('.hero-card').waitFor();assert.equal(await page.locator('.hero-card').count(),1);assert.equal(await page.locator('.unknown-db-row').count(),1);pass('Mixed workspace keeps hero cards and unknown DB list together');}
  await page.locator('.sweep-button').click();await page.waitForFunction(()=>document.querySelectorAll('.hero-card').length>1&&!document.querySelector('.unknown-db-panel'));pass('Clear workspace removes unknown packages and restores the catalog');
  assert.deepEqual(errors,[]);for(const [name,value] of Object.entries(originals))assert.equal(hash(path.join(source,name)),value);pass('No frontend errors; original DBs and record.bytes unchanged');
  const report={date:new Date().toISOString(),count:loaded.count,types:loaded.types,parseWarnings:loaded.errors.length,results,temp};fs.writeFileSync(path.join(temp,'report.json'),JSON.stringify(report,null,2));console.log(JSON.stringify(report,null,2));
 }finally{if(browser)await browser.close();if(app)app.kill();worker.kill();for(const item of pending.values())clearTimeout(item.timer)}
}
main().catch(e=>{console.error(e);process.exitCode=1});
