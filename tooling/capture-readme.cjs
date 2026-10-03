const {chromium}=require('playwright-core');
const fs=require('node:fs'),path=require('node:path'),cp=require('node:child_process');
const root=path.resolve(__dirname,'..'),out=path.join(root,'docs/images');
const pause=ms=>new Promise(r=>setTimeout(r,ms));
async function main(){
 fs.mkdirSync(out,{recursive:true});
 try{const r=await fetch('http://127.0.0.1:9224/json/version');if(r.ok)throw Error('Smoke port already in use');}catch(e){if(!e.message.includes('fetch failed'))throw e;}
 const exe=path.join(root,'build/HOK-BetaStudio-1.2-rebuild-beta-win-x64/HOK BetaStudio.exe');
 const proc=cp.spawn(exe,['--smoke'],{cwd:path.dirname(exe),windowsHide:true,stdio:'ignore'});let browser;
 try{
  for(let i=0;i<100;i++){try{browser=await chromium.connectOverCDP('http://127.0.0.1:9224');break}catch{await pause(200)}}
  if(!browser)throw Error('Desktop connection timed out');
  const context=browser.contexts()[0];let page;
  for(let i=0;i<100;i++){page=context.pages().find(p=>p.url().startsWith('https://hok.local/'));if(page)break;await pause(100)}
  const cdp=await context.newCDPSession(page);
  await cdp.send('Emulation.setDeviceMetricsOverride',{width:1440,height:1000,deviceScaleFactor:1,mobile:false});
  await page.waitForFunction(()=>document.querySelectorAll('.hero-card').length===133);
  await page.locator('.title-right select').selectOption('en');
  async function shot(name){await pause(1600);await page.screenshot({path:path.join(out,name+'.png')});console.log(name);}
  await shot('01-catalog');
  const data={items:[],files:['3200010500','3200010504'].map(p=>path.join(root,p)),dragOperationsMask:1};
  for(const type of ['dragEnter','dragOver','drop'])await cdp.send('Input.dispatchDragEvent',{type,x:500,y:300,data});
  await page.waitForFunction(()=>document.querySelectorAll('.hero-card').length===1&&!document.querySelector('.statusbar .spinner'));
  await page.locator('.hero-card').click();await shot('02-skins');
  await page.locator('.skin-card').filter({hasText:'10500'}).click();
  await page.waitForFunction(()=>document.querySelector('.pagination')?.textContent.includes('841'));await shot('03-assets');
  await page.locator('.asset-toolbar select').selectOption('Texture2D');await page.locator('.asset-name').first().click();
  await page.waitForSelector('.image-preview canvas');await shot('04-texture');await page.locator('.close-button').click();
  await page.locator('.asset-toolbar select').selectOption('Mesh');await page.locator('.asset-name').first().click();
  await page.waitForSelector('.canvas-host canvas');await shot('05-model');await page.locator('.close-button').click();
  await page.locator('.skin-card').filter({hasText:'10504'}).click();
  await page.waitForFunction(()=>document.querySelector('.pagination')?.textContent.includes('864'));
  await page.locator('.asset-toolbar select').selectOption('WwiseBank');await page.locator('.asset-name').first().click();
  await page.waitForFunction(()=>document.querySelectorAll('.bank-leaf').length===6&&document.querySelector('audio')?.readyState>=1);
  await shot('06-audio-bank');await page.locator('.close-button').click();
  await page.locator('tbody input[type=checkbox]').first().check();
  await page.getByRole('button',{name:'Export selected',exact:true}).click();await shot('07-export');
 }finally{if(browser)await browser.close();proc.kill();}
}
main().catch(e=>{console.error(e);process.exitCode=1});
