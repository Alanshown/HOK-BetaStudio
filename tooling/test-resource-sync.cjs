// Own-app integration test. Starts only an isolated hidden smoke instance;
// never modifies the user's normal LocalAppData catalog or source DB packages.
const {chromium}=require('playwright-core');
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto'),{spawn}=require('node:child_process');
const root=path.resolve(__dirname,'..');
const build=path.resolve(root,process.argv[2]||'build/HOK-BetaStudio-1.3-win-x64');
const data=path.join(root,'.cache','tests','resource-sync-'+crypto.randomUUID());
const seed=JSON.parse(fs.readFileSync(path.join(root,'assets/catalog/remote-index.seed.json'),'utf8'));
const baseline=structuredClone(seed);baseline.records.find(e=>e.kind==='hero'&&e.id==='105').name+=' · index test';
fs.mkdirSync(path.join(data,'catalog'),{recursive:true});fs.writeFileSync(path.join(data,'catalog','index.json'),JSON.stringify(baseline));
const results=[],assertions=(value,text)=>{assert(value,text);results.push(text);console.log('PASS '+text)};
const dbRoot=path.join(root,'3200010500');
const originals=fs.readdirSync(dbRoot).filter(n=>n.endsWith('.db')).map(n=>({file:path.join(dbRoot,n),hash:crypto.createHash('sha256').update(fs.readFileSync(path.join(dbRoot,n))).digest('hex')}));
function hash(file){return crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex')}
async function main(){
 let processApp,browser;
 try{
  try{await fetch('http://127.0.0.1:9224/json/version');throw new Error('Debug port 9224 is occupied. Close the other smoke test first.')}catch(e){if(e.message.includes('occupied'))throw e}
  processApp=spawn(path.join(build,'HOK BetaStudio.exe'),['--smoke','--smoke-data='+data],{cwd:build,windowsHide:true,stdio:'ignore'});
  for(let n=0;n<100;n++){try{browser=await chromium.connectOverCDP('http://127.0.0.1:9224');break}catch{await new Promise(r=>setTimeout(r,200))}}
  assert(browser,'Smoke app failed to start');
  let page;
  for(let n=0;n<80;n++){page=browser.contexts()[0].pages().find(p=>p.url().startsWith('https://hok.local/'));if(page)break;await new Promise(r=>setTimeout(r,200))}
  assert(page);page.setDefaultTimeout(30000);
  const errors=[];page.on('pageerror',e=>errors.push(e.message));
  const cdp=await page.context().newCDPSession(page);
  await cdp.send('Emulation.setDeviceMetricsOverride',{width:1440,height:920,deviceScaleFactor:1,mobile:false});
  await page.waitForFunction(()=>document.querySelectorAll('.hero-card').length===133);
  await page.locator('.title-right select').selectOption('zh');
  assertions((await page.locator('.version').innerText())==='1.3','desktop version is 1.3');
  assertions((await page.locator('.hero-card img').first().getAttribute('src')).startsWith('https://game-1255653016.file.myqcloud.com/'),'hero portraits request official HTTPS image links');
  await page.waitForFunction(()=>[...document.querySelectorAll('.hero-card img')].slice(0,6).every(e=>e.complete&&e.naturalWidth>0));
  assertions(await page.locator('.hero-card img').first().evaluate(e=>e.naturalWidth>0&&e.src.startsWith('https://game-1255653016.file.myqcloud.com/')),'actual CDN portrait loaded (not a placeholder)');
  await page.locator('.resource-sync').waitFor({timeout:110000});
  assertions((await page.locator('.resource-sync').getAttribute('title')).includes('变更 1 项'),'only validated differing index reveals sync button');
  assertions((await page.locator('.statusbar').innerText()).includes('英雄图鉴'),'background index checking never changes the status bar');
  await page.waitForTimeout(1800);
  await page.screenshot({path:path.join(data,'sync-available-1.3.png')});
  for(const [lang,label] of [['en','Resource sync'],['vi','Đồng bộ tài nguyên'],['zh','资源同步']]){await page.locator('.title-right select').selectOption(lang);assert.equal(await page.locator('.resource-sync').getAttribute('aria-label'),label)}
  results.push('sync button supports Chinese, English and Vietnamese');
  const drop={items:[],files:[dbRoot],dragOperationsMask:1};
  for(const type of ['dragEnter','dragOver','drop'])await cdp.send('Input.dispatchDragEvent',{type,x:500,y:300,data:drop});
  await page.waitForFunction(()=>document.querySelectorAll('.hero-card').length===1);
  await page.locator('.hero-card').evaluate(e=>e.click());
  await page.waitForFunction(()=>document.querySelectorAll('.skin-card').length===1);
  assertions((await page.locator('.skin-card').innerText()).includes('默认皮肤'),'real 10500 DB pair still maps to default skin');
  assert.equal(await page.locator('.skin-card img').getAttribute('src'),await page.locator('.heading-portrait').getAttribute('src'));
  await page.locator('.skin-card').evaluate(e=>e.click());
  await page.waitForFunction(()=>document.querySelector('.pagination')?.textContent.includes('841'),{},{timeout:90000});
  assertions(true,'real default-skin DB loads 841 assets with remote catalog enabled');
  const cache=path.join(data,'cache'),session=fs.readdirSync(cache).find(n=>/^[0-9a-f]{32}$/.test(n));assert(session);
  const cacheMarker=path.join(cache,session,'owned-cache-test.bin');fs.writeFileSync(cacheMarker,'test-owned-cache');
  const editing=path.join(cache,'editing',session);fs.mkdirSync(editing,{recursive:true});const editMarker=path.join(editing,'staged-edit-test.bin');fs.writeFileSync(editMarker,'test-staged-edit');
  // Keep an output outside the cache to check that synchronization does not touch exports.
  const exportMarker=path.join(data,'user-export-test.bin');fs.writeFileSync(exportMarker,'user-owned-export');
  const previousRevision=JSON.parse(fs.readFileSync(path.join(data,'catalog','candidate.json'),'utf8')).index;
  await page.evaluate(()=>{window.__domino=[];new MutationObserver(()=>{document.querySelectorAll('.hero-card').forEach(e=>{if(!e.dataset.observed){e.dataset.observed='1';window.__domino.push(getComputedStyle(e).animationName)}})}).observe(document.querySelector('.workspace'),{childList:true,subtree:true})});
  await page.locator('.resource-sync').evaluate(e=>e.click());
  await page.waitForFunction(()=>document.querySelectorAll('.hero-card').length===133&&!document.querySelector('.resource-sync'));
  assertions(!fs.existsSync(cacheMarker),'sync removes imported DB session cache');
  assertions(!fs.existsSync(editMarker),'sync removes staged editing cache for the same session');
  assertions(fs.readFileSync(exportMarker,'utf8')==='user-owned-export'&&originals.every(e=>hash(e.file)===e.hash),'sync preserves source DBs and already-exported files');
  assertions(await page.locator('.asset-panel,.skin-card,.sweep-button,.replacement-mark,.rebuild-button').count()===0,'sync resets hero navigation, assets, selection and replacement UI');
  assertions((await page.locator('.hero-card').first().innerText()).includes('廉颇')&&!(await page.locator('.workspace').innerText()).includes('index test'),'new index names applied only after user click');
  assertions(await page.evaluate(()=>window.__domino.includes('portrait-domino')),'new hero grid replays domino animation');
  const layout=await page.locator('.hero-grid').evaluate(e=>({cols:getComputedStyle(e).gridTemplateColumns.split(' ').length,delays:[...e.children].slice(0,30).map(c=>parseFloat(c.style.getPropertyValue('--delay')))}));
  assertions(layout.cols>1&&layout.delays[1]===38&&layout.delays[layout.cols]===38,'domino wave follows diagonal row + column order');
  const disk=JSON.parse(fs.readFileSync(path.join(data,'catalog','index.json'),'utf8'));
  assertions(JSON.stringify(disk)===JSON.stringify(previousRevision),'validated candidate atomically becomes local applied index');
  assertions(fs.existsSync(path.join(data,'catalog','index.backup.json')),'previous index backup remains outside DB cache');
  await page.locator('.hero-card').first().evaluate(e=>e.click());
  assertions(await page.locator('.skin-card').first().evaluate(e=>getComputedStyle(e).animationName==='portrait-domino'),'skin grid retains domino entry after resource sync');
  await page.getByRole('button',{name:'返回',exact:true}).evaluate(e=>e.click());
  await page.emulateMedia({reducedMotion:'reduce'});
  assertions(await page.locator('.hero-card').first().evaluate(e=>getComputedStyle(e).animationName==='none'),'system reduced-motion preference remains accessible');
  await page.emulateMedia({reducedMotion:'no-preference'});
  await page.waitForTimeout(1800);
  await page.waitForFunction(()=>[...document.querySelectorAll('.hero-card img')].slice(0,6).every(e=>e.complete&&e.naturalWidth>0));
  assertions(await page.locator('.hero-card img').first().evaluate(e=>e.src.startsWith('https://game-1255653016.file.myqcloud.com/')),'post-sync portraits reload from the CDN after browser cache cleanup');
  await page.screenshot({path:path.join(data,'desktop-1.3.png')});
  await page.reload();await page.waitForFunction(()=>document.querySelectorAll('.hero-card').length===133);await page.waitForTimeout(2200);
  assertions(await page.locator('.resource-sync,[role="alert"]').count()===0,'reopening the current index shows no sync button or background notification');
  assertions(errors.length===0,'no frontend runtime errors');
  console.log(JSON.stringify({passed:results.length,results,data,screenshot:path.join(data,'desktop-1.3.png')},null,2));
 }finally{if(browser)await browser.close();if(processApp)processApp.kill()}
}
main().catch(e=>{console.error(e);process.exitCode=1});
