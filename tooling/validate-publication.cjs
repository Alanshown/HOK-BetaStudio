const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict'),http=require('node:http');
const {chromium}=require('playwright-core');
const root=path.resolve(__dirname,'..'),docs=path.join(root,'docs');
async function main(){
 const reads=['README.md','README.zh.md','README.vi.md'].map(p=>fs.readFileSync(path.join(root,p),'utf8'));
 const code=s=>Array.from(s.matchAll(/```[^\n]*\n([\s\S]*?)```/g),m=>m[1]);
 for(const [i,s]of reads.entries()){
  assert.equal((s.match(/README-I18N:START/g)||[]).length,1);assert.equal((s.match(/README-I18N:END/g)||[]).length,1);
  assert.deepEqual(code(s),code(reads[0]));
  for(const m of s.matchAll(/\]\(#([^)]*)\)/g))assert(s.includes('id="'+m[1]+'"'),m[1]);
  for(const m of s.matchAll(/(?:\]\(|src=")([^\s)"#]+)(?:\)|")/g)){if(!/^(https?:|#)/.test(m[1]))assert(fs.existsSync(path.join(root,m[1])),m[1])}
  assert(!/[CD]:[\\/]Users|Nox_share/.test(s),'Private path in README '+i);
 }
 const server=http.createServer((req,res)=>{const pathname=decodeURIComponent(new URL(req.url,'http://localhost').pathname),file=path.resolve(docs,'.'+pathname);if(!file.startsWith(docs+path.sep)){res.writeHead(403).end();return}if(!fs.existsSync(file)){res.writeHead(404).end();return}res.setHeader('Content-Type',({'.html':'text/html; charset=utf-8','.js':'application/javascript; charset=utf-8','.css':'text/css','.png':'image/png','.ttf':'font/ttf'})[path.extname(file)]||'application/octet-stream');fs.createReadStream(file).pipe(res)});
 await new Promise(r=>server.listen(0,'127.0.0.1',r));let browser;
 try{
  browser=await chromium.launch({channel:'msedge',headless:true});const page=await browser.newPage({viewport:{width:1440,height:1000}});const errors=[];page.on('pageerror',e=>errors.push(e.message));
  await page.goto('http://127.0.0.1:'+server.address().port+'/index.html');await page.evaluate(()=>document.fonts.ready);assert.equal(await page.locator('[role=tab]').count(),7);
  for(const lang of ['en','zh','vi']){
   await page.locator('#language').selectOption(lang);
   for(let i=0;i<7;i++){await page.locator('[role=tab]').nth(i).click();await page.waitForFunction(()=>{const i=document.querySelector('#screenshot');return i.complete&&i.naturalWidth>=1440});assert.equal(await page.locator('#position').innerText(),(i+1)+' / 7');}
   await page.locator('#screenshot-open').click();assert(await page.locator('dialog').isVisible());await page.keyboard.press('Escape');assert(!await page.locator('dialog').isVisible());
   await page.setViewportSize({width:390,height:844});assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),'Mobile horizontal overflow '+lang);await page.setViewportSize({width:1440,height:1000});
  }
  assert.deepEqual(errors,[]);console.log(JSON.stringify({readmeLanguages:3,matchingCodeBlocks:true,anchorsAndFiles:true,screenshotStates:21,lightboxLanguages:3,mobileLanguages:3,runtimeErrors:errors.length}));
 }finally{if(browser)await browser.close();server.close()}
}
main().catch(e=>{console.error(e);process.exitCode=1});
