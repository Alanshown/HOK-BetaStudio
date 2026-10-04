// Import-only real desktop regression; never replaces/rebuilds the input DBs.
const {chromium}=require('playwright-core');
const fs=require('node:fs'),path=require('node:path'),cp=require('node:child_process'),readline=require('node:readline'),assert=require('node:assert/strict'),crypto=require('node:crypto');
const root=path.resolve(__dirname,'..'),source=path.resolve(process.argv[2]||''),build=path.resolve(root,process.argv[3]||'build/HOK-BetaStudio-1.3-reference-fix-win-x64');
assert(process.argv[2],'Usage: node tooling/test-folder-import.cjs <DB folder> [build]');
const temp=fs.mkdtempSync(path.join(root,'.cache','folder-import-')),pause=ms=>new Promise(r=>setTimeout(r,ms));
const files=fs.readdirSync(source,{recursive:true}).map(n=>path.join(source,n)).filter(n=>fs.statSync(n).isFile());
const hash=f=>crypto.createHash('sha256').update(fs.readFileSync(f)).digest('hex');
const before=Object.fromEntries(files.map(f=>[f,hash(f)]));
const pending=new Map();let serial=0,worker,app,browser;
async function main(){
 try{
  worker=cp.spawn(path.join(build,'worker','Hok.Worker.exe'),[path.join(temp,'worker-cache')],{cwd:path.join(build,'worker'),windowsHide:true,stdio:['pipe','pipe','pipe']});worker.stderr.resume();
  readline.createInterface({input:worker.stdout}).on('line',line=>{const r=JSON.parse(line),p=pending.get(r.id);if(p){clearTimeout(p.timer);pending.delete(r.id);r.ok?p.resolve(r.data):p.reject(Error(r.error.message))}});
  const rpc=(method,payload)=>new Promise((resolve,reject)=>{const id=String(++serial),timer=setTimeout(()=>reject(Error('Timeout '+method)),180000);pending.set(id,{resolve,reject,timer});worker.stdin.write(JSON.stringify({id,method,payload})+'\n')});
  const loaded=await rpc('load',{paths:files.filter(f=>/\.db$/i.test(f))});assert(loaded.count>0);
  console.log(JSON.stringify({count:loaded.count,types:loaded.types,diagnostics:loaded.errors.length}));
  fs.writeFileSync(path.join(temp,'diagnostics.json'),JSON.stringify(loaded.errors,null,2));
  const all=[];for(let page=0;all.length<loaded.count;page++){const r=await rpc('list',{page});assert(r.items.length);all.push(...r.items)}
  const previews=[];
  for(const type of ['Texture2D','Mesh','WwiseAudio']){const a=all.find(a=>a.type===type);if(!a)continue;try{const r=await rpc('preview',{assetId:a.id});previews.push({type,ok:!!r})}catch(e){previews.push({type,ok:false,error:e.message})}}
  try{await fetch('http://127.0.0.1:9224/json/version');throw Error('Smoke port occupied')}catch(e){if(e.message==='Smoke port occupied')throw e}
  app=cp.spawn(path.join(build,'HOK BetaStudio.exe'),['--smoke','--smoke-data='+path.join(temp,'desktop-data')],{cwd:build,windowsHide:true,stdio:'ignore'});
  for(let i=0;i<150;i++){try{browser=await chromium.connectOverCDP('http://127.0.0.1:9224');break}catch{await pause(200)}}assert(browser);
  let page;for(let i=0;i<150;i++){page=browser.contexts()[0].pages().find(p=>p.url().startsWith('https://hok.local/'));if(page)break;await pause(100)}assert(page);page.setDefaultTimeout(180000);
  const errors=[];page.on('pageerror',e=>errors.push(e.message));await page.waitForFunction(()=>document.querySelectorAll('.hero-card').length>0);await page.locator('.title-right select').selectOption('zh');
  const cdp=await page.context().newCDPSession(page);await cdp.send('Emulation.setDeviceMetricsOverride',{width:1440,height:920,deviceScaleFactor:1,mobile:false});
  for(const type of ['dragEnter','dragOver','drop'])await cdp.send('Input.dispatchDragEvent',{type,x:500,y:300,data:{items:[],files:[source],dragOperationsMask:1}});
  await page.locator('.unknown-db-row').first().waitFor();await page.locator('.unknown-db-row').first().click();
  await page.waitForFunction(n=>document.querySelector('.pagination')?.textContent.startsWith(String(n)),loaded.count);
  await page.screenshot({path:path.join(temp,'imported-assets.png')});
  if(process.argv.includes('--animation')){
   await page.locator('.asset-toolbar select').selectOption('Sprite');
   await page.waitForFunction(()=>document.querySelector('tbody tr td:nth-child(3)')?.textContent?.startsWith('Sprite'));
   await page.locator('.asset-name').first().click();await page.locator('.image-preview canvas').waitFor();
   await page.screenshot({path:path.join(temp,'sprite-preview.png')});await page.locator('.close-button').click();
   await page.locator('.asset-toolbar select').selectOption('AnimationClip');
   await page.waitForFunction(()=>document.querySelector('tbody tr td:nth-child(3)')?.textContent?.startsWith('AnimationClip'));
   await page.locator('.asset-name').filter({hasText:'Take 001'}).first().click();
   await page.locator('[data-animation-ready="true"]').waitFor();
   const seek=page.getByRole('slider',{name:'播放进度'});
   await page.waitForFunction(()=>Number(document.querySelector('.model-stage input[type=range]')?.value)>0);
   await page.getByRole('button',{name:'暂停',exact:true}).click();await pause(150);const paused=await seek.inputValue();await pause(300);assert.equal(await seek.inputValue(),paused);
   await page.getByRole('button',{name:'播放',exact:true}).click();await pause(400);assert.notEqual(await seek.inputValue(),paused);
   await page.screenshot({path:path.join(temp,'animation-preview.png')});
   await page.locator('.close-button').click();console.log('PASS sprite image plus linked animated model; auto-play, pause, resume and cleanup');
  }
  assert.deepEqual(errors,[]);for(const [f,h]of Object.entries(before))assert.equal(hash(f),h);
  const report={source,count:loaded.count,types:loaded.types,diagnostics:loaded.errors.length,previews,desktopLoaded:true,originalsUnchanged:true,temp};
  fs.writeFileSync(path.join(temp,'report.json'),JSON.stringify(report,null,2));console.log(JSON.stringify(report,null,2));
 }finally{if(browser)await browser.close();if(app)app.kill();if(worker)worker.kill();for(const p of pending.values())clearTimeout(p.timer)}
}
main().catch(e=>{console.error(e);process.exitCode=1});
