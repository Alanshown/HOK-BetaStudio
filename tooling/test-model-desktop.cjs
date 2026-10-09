const {chromium}=require('playwright-core'),assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),cp=require('node:child_process');
const [buildArg,sourceArg,outArg]=process.argv.slice(2),build=path.resolve(buildArg),source=path.resolve(sourceArg),out=path.resolve(outArg),profile=path.join(out,'profile'),exportsDir=path.join(out,'exports');
const pause=ms=>new Promise(r=>setTimeout(r,ms));
(async()=>{
 try{if((await fetch('http://127.0.0.1:9224/json/version')).ok)throw Error('Smoke port already occupied');}catch(e){if(!e.message.includes('fetch failed'))throw e;}
 fs.mkdirSync(out,{recursive:true});const app=cp.spawn(path.join(build,'HOK BetaStudio.exe'),['--smoke','--smoke-data='+profile,'--smoke-output='+exportsDir,source],{cwd:build,windowsHide:true,stdio:'ignore'});let browser;
 const report={source,build,checks:[],errors:[]};
 try{
  for(let i=0;i<150;i++){try{browser=await chromium.connectOverCDP('http://127.0.0.1:9224');break}catch{await pause(200)}}assert(browser);
  const context=browser.contexts()[0];let page;for(let i=0;i<100;i++){page=context.pages().find(p=>p.url().startsWith('https://hok.local/'));if(page)break;await pause(100)}assert(page);page.setDefaultTimeout(120000);page.on('pageerror',e=>report.errors.push(e.message));
  await(await context.newCDPSession(page)).send('Emulation.setDeviceMetricsOverride',{width:1440,height:1000,deviceScaleFactor:1,mobile:false});
  await page.waitForFunction(()=>document.querySelector('.sweep-button')&&!document.querySelector('.statusbar .spinner'));await page.locator('.title-right select').selectOption('en');await page.locator('.hero-card').first().evaluate(e=>e.click());await page.locator('.skin-card').first().evaluate(e=>e.click());
  await page.waitForFunction(()=>document.querySelector('.asset-toolbar')&&!document.querySelector('.statusbar .spinner'));await page.locator('.asset-toolbar select').selectOption('Mesh');await page.waitForFunction(()=>document.querySelector('tbody tr td:nth-child(3)')?.textContent.startsWith('Mesh'));
  await page.locator('tbody input[type=checkbox]').first().check();
  for(const format of ['fbx','obj']){
   await page.getByRole('button',{name:'Export selected',exact:true}).click();assert(await page.locator('.export-form option[value=fbx]').count());assert(await page.locator('.export-form').innerText().then(s=>s.includes('OBJ contains static geometry')));await page.locator('.export-form select').selectOption(format);await page.locator('.modal-actions .primary').click();await page.locator('.export-results').waitFor();assert.equal(await page.locator('.export-results .failure').count(),0);assert((await page.locator('.export-results').innerText()).includes('.'+format));
   if(format==='fbx')assert((await page.locator('.export-results').innerText()).includes('no animation takes'),'No-animation warning must be visible');
   await page.screenshot({path:path.join(out,format+'-export.png')});await page.locator('.close-button').click();report.checks.push(format.toUpperCase()+' exported through the actual desktop bridge');
  }
  const reports=fs.readdirSync(exportsDir).filter(n=>/^export-.*\.json$/.test(n)).map(n=>JSON.parse(fs.readFileSync(path.join(exportsDir,n),'utf8')));assert.equal(reports.length,2);for(const r of reports){assert.equal(r.success,1);assert.equal(r.failed,0);assert.equal(r.rawFallback,0);assert(fs.statSync(r.results[0].path).size>100);}
  assert(!fs.existsSync(path.join(profile,'preview.log')),'Export should not start preview worker');assert.deepEqual(report.errors,[]);report.checks.push('Warnings displayed, actual output files exist, no preview worker or WebView exceptions');console.log(JSON.stringify(report));
 }finally{if(browser)await browser.close();app.kill();fs.writeFileSync(path.join(out,'report.json'),JSON.stringify(report,null,2));}
})().catch(e=>{console.error(e);process.exitCode=1});
