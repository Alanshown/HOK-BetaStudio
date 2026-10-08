const {chromium}=require('playwright-core'),assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),cp=require('node:child_process'),crypto=require('node:crypto');
const root=path.resolve(__dirname,'..'),[buildArg,sourceArg,outArg,mode='bank']=process.argv.slice(2),build=path.resolve(buildArg),source=path.resolve(sourceArg),out=path.resolve(outArg),profile=path.join(out,'profile');
const corpus=fs.readFileSync(path.join(root,'tests/hok_db_corpus.txt'),'utf8').trim().split(/\r?\n/);assert(corpus.some(d=>path.resolve(d).toLowerCase()===path.dirname(source).toLowerCase())&&source.endsWith('.db'));
const pause=ms=>new Promise(r=>setTimeout(r,ms)),hash=async f=>{const h=crypto.createHash('sha256');for await(const b of fs.createReadStream(f))h.update(b);return h.digest('hex')};
function files(dir){return fs.existsSync(dir)?fs.readdirSync(dir,{withFileTypes:true}).flatMap(e=>e.isDirectory()?files(path.join(dir,e.name)):[path.join(dir,e.name)]):[]}
async function main(){
 try{if((await fetch('http://127.0.0.1:9224/json/version')).ok)throw Error('Another smoke session is active');}catch(e){if(!e.message.includes('fetch failed'))throw e;}
 fs.mkdirSync(out,{recursive:true});const report={source,mode,started:new Date().toISOString(),checks:[],errors:[]},before=await hash(source);
 const proc=cp.spawn(path.join(build,'HOK BetaStudio.exe'),['--smoke','--smoke-data='+profile,source],{cwd:build,windowsHide:true,stdio:'ignore'});let browser;
 const pass=s=>{report.checks.push(s);console.log('PASS',s)};
 try{
  for(let i=0;i<150;i++){try{browser=await chromium.connectOverCDP('http://127.0.0.1:9224');break}catch{await pause(200)}}assert(browser);const context=browser.contexts()[0];let page;for(let i=0;i<100;i++){page=context.pages().find(p=>p.url().startsWith('https://hok.local/'));if(page)break;await pause(100)}assert(page);page.setDefaultTimeout(180000);page.on('pageerror',e=>report.errors.push(e.message));
  await(await context.newCDPSession(page)).send('Emulation.setDeviceMetricsOverride',{width:1440,height:1000,deviceScaleFactor:1,mobile:false});
  await page.waitForFunction(()=>document.querySelector('.sweep-button')&&!document.querySelector('.statusbar .spinner'));await page.locator('.title-right select').selectOption('en');
  if(await page.locator('.unknown-db-row').count())await page.locator('.unknown-db-row').first().evaluate(e=>e.click());else{await page.locator('.hero-card').first().evaluate(e=>e.click());await page.locator('.skin-card').first().evaluate(e=>e.click())}
  await page.waitForFunction(()=>document.querySelector('.asset-toolbar')&&!document.querySelector('.statusbar .spinner'),null,{timeout:180000});
  assert(!fs.existsSync(path.join(profile,'preview.log')));assert(files(path.join(profile,'cache')).every(f=>f.endsWith('.cache')));pass('import and list rendering create no preview worker or media files');
  assert(await page.evaluate(()=>!performance.getEntriesByType('resource').some(r=>/\/(?:Preview|ImagePreview|FontPreview|CurvePreview|BankPreview|AudioPlayer)-[^/]+\.js/.test(r.name))));pass('preview modules, including the 3D renderer, are not loaded at startup');
  async function select(type){assert(await page.locator(`.asset-toolbar option[value="${type}"]`).count(),'Sample does not contain '+type);await page.locator('.asset-toolbar select').selectOption(type);await page.waitForFunction(type=>document.querySelector('.asset-toolbar select')?.value===type&&!document.querySelector('.statusbar .spinner')&&document.querySelector('tbody tr'),type)}
  async function openFirst(){await page.locator('tbody .asset-name').first().evaluate(e=>e.click())}
  if(mode==='bank'){
   await select('WwiseBank');await openFirst();await page.locator('.bank-leaf').first().waitFor();assert.equal(await page.locator('audio').count(),0);assert(!files(path.join(profile,'cache')).some(f=>/\.(wav|mp3|wem)$/i.test(f)));await page.screenshot({path:path.join(out,'bank-not-loaded.png')});pass('opening BNK indexes its children but does not preload any audio');
   await page.locator('.bank-leaf').first().evaluate(e=>e.click());await page.waitForFunction(()=>{const a=document.querySelector('audio');return a&&a.readyState>=1&&Number.isFinite(a.duration)&&a.duration>0});await page.locator('audio').evaluate(a=>{a.muted=true});await page.locator('.audio-play').evaluate(e=>e.click());await page.waitForFunction(()=>document.querySelector('audio')?.currentTime>0.05);await page.locator('.audio-play').evaluate(e=>e.click());assert.equal(files(path.join(profile,'cache')).filter(f=>f.endsWith('.wav')).length,1);await page.screenshot({path:path.join(out,'selected-audio.png')});pass('only the clicked audio is decoded and plays in WebView2');
  }else if(mode==='assets'||mode==='animation'){
   let checked=0;
   for(const type of (mode==='animation'?['AnimationClip']:['Texture2D','Mesh','AnimationClip'])){
    if(!await page.locator(`.asset-toolbar option[value="${type}"]`).count())continue;
    await select(type);await openFirst();
    if(type==='Texture2D')await page.waitForFunction(()=>document.querySelector('.image-preview canvas')?.width>0);
    else if(type==='Mesh')await page.waitForFunction(()=>document.querySelector('.model-stage canvas')&&!document.querySelector('.model-stage .stage-state'));
    else await page.waitForFunction(()=>document.querySelector('[data-curve-preview=true]')||document.querySelector('[data-animation-ready=true]'));
    if(type==='Texture2D')await page.locator('.image-preview canvas').evaluate(c=>window.__closedCanvas=c);
    if(type==='Texture2D')assert(await page.evaluate(()=>!performance.getEntriesByType('resource').some(r=>/\/Preview-[^/]+\.js/.test(r.name))),'Image preview must not load the 3D renderer');
    if(type==='Mesh')await page.locator('.model-stage canvas').evaluate(c=>window.__closedModelCanvas=c);
    const other=await page.locator('tbody .parse-status').allTextContents();assert(other.slice(1).every(s=>s==='Decoded on demand'||type==='Texture2D'),'Unclicked heavy assets must remain deferred');
    await page.screenshot({path:path.join(out,type+'.png')});await page.locator('.close-button').evaluate(e=>e.click());
    if(type==='Texture2D')assert(await page.evaluate(()=>window.__closedCanvas.width===0&&window.__closedCanvas.height===0));
    if(type==='Mesh')assert(await page.evaluate(()=>!window.__closedModelCanvas.isConnected&&window.__closedModelCanvas.getContext('webgl2').isContextLost()));
    for(let i=0;i<150&&files(path.join(profile,'cache')).some(f=>!f.endsWith('.cache'));i++)await pause(100);
    assert(files(path.join(profile,'cache')).every(f=>f.endsWith('.cache')));assert.equal(await page.locator('dialog').count(),0);pass(type+' loads only on click and releases its preview on close');checked++;
   }
   assert(checked>0,'Sample has no requested preview types');
  }else{
   await select('Font');assert((await page.locator('tbody tr').first().innerText()).includes('Decoded on demand'));assert.equal(await page.evaluate(()=>[...document.fonts].filter(f=>f.family.startsWith('hok-preview-')).length),0);await openFirst();await page.locator('[data-font-ready=true]').waitFor({timeout:180000});assert.equal(await page.evaluate(()=>[...document.fonts].filter(f=>f.family.startsWith('hok-preview-')).length),1);await page.screenshot({path:path.join(out,'selected-font.png')});pass('font bytes are decoded only on click and exactly one runtime FontFace is loaded');
  }
  if(await page.locator('.close-button').count())await page.locator('.close-button').evaluate(e=>e.click());await page.waitForFunction(()=>!document.querySelector('dialog')&&![...document.fonts].some(f=>f.family.startsWith('hok-preview-')));for(let i=0;i<100&&files(path.join(profile,'cache')).some(f=>!f.endsWith('.cache'));i++)await pause(100);
  assert(files(path.join(profile,'cache')).every(f=>f.endsWith('.cache')));pass('closing preview removes media artifacts and browser font/audio resources');
  assert.deepEqual(report.errors,[]);pass('no WebView exceptions');
 }catch(e){report.error=e.stack;throw e}finally{if(browser)await browser.close();proc.kill();report.originalUnchanged=before===await hash(source);report.finished=new Date().toISOString();fs.writeFileSync(path.join(out,'report.json'),JSON.stringify(report,null,2));}
}
main().catch(e=>{console.error(e);process.exitCode=1});
