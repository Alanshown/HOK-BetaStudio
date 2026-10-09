// Run against a separately launched --smoke instance, never a user's browser.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const output=path.resolve(process.argv[2]||'.cache/packaged-version');fs.mkdirSync(output,{recursive:true});
const expected=require('../frontend/package.json').version.split('.').slice(0,2).join('.');
(async()=>{
 let target;for(let i=0;i<40;i++){try{target=(await(await fetch('http://127.0.0.1:9224/json/list')).json()).find(p=>p.url==='https://hok.local/index.html');if(target)break;}catch{}await new Promise(r=>setTimeout(r,250));}assert(target,'Isolated desktop smoke target missing');
 const socket=new WebSocket(target.webSocketDebuggerUrl),pending=new Map();let serial=0;
 await new Promise((resolve,reject)=>{socket.onopen=resolve;socket.onerror=reject;});
 socket.onmessage=e=>{const message=JSON.parse(e.data),p=pending.get(message.id);if(p){pending.delete(message.id);clearTimeout(p.timer);message.error?p.reject(Error(JSON.stringify(message.error))):p.resolve(message.result);}};
 const call=(method,params={})=>new Promise((resolve,reject)=>{const id=++serial,timer=setTimeout(()=>reject(Error('CDP timeout: '+method)),10000);pending.set(id,{resolve,reject,timer});socket.send(JSON.stringify({id,method,params}));});
 const evaluate=async expression=>{const r=await call('Runtime.evaluate',{expression,returnByValue:true,awaitPromise:true});assert(!r.exceptionDetails,JSON.stringify(r.exceptionDetails));return r.result.value;};
 try{
  let actual;for(let i=0;i<40;i++){actual=await evaluate(`document.querySelector('.version')?.textContent`);if(actual)break;await new Promise(r=>setTimeout(r,250));}assert.equal(actual,expected);
  assert.equal(await evaluate(`document.querySelector('.wordmark')?.textContent`),'HOK BetaStudio');
  for(const language of ['en','vi','zh']){await evaluate(`(()=>{const s=document.querySelector('.title-right select');s.value=${JSON.stringify(language)};s.dispatchEvent(new Event('change',{bubbles:true}));})()`);await new Promise(r=>setTimeout(r,100));assert.equal(await evaluate(`document.querySelector('.version')?.textContent`),expected);}
  const screenshot=await call('Page.captureScreenshot',{format:'png'});fs.writeFileSync(path.join(output,'version-1.4.png'),Buffer.from(screenshot.data,'base64'));
  const result={passed:true,expected,actual,languages:['en','vi','zh'],target:target.url,scope:'Packaged native WebView2 header/version, isolated smoke profile'};fs.writeFileSync(path.join(output,'report.json'),JSON.stringify(result,null,2));console.log(JSON.stringify(result));
 }finally{socket.close();}
})().catch(e=>{console.error(e);process.exitCode=1});
