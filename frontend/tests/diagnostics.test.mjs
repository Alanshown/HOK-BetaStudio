import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import test from 'node:test';
import {assetDiagnosticTitle,hasLoadDiagnostics,parseStatusText} from '../src/diagnostics.ts';

const read=(path)=>readFileSync(new URL(path,import.meta.url),'utf8');
const locales=Object.fromEntries(['en','zh','vi'].map(lang=>[lang,JSON.parse(read(`../src/locales/${lang}.json`))]));
const statuses=['typed-complete','typed-partial','generic-raw','parser-disabled','parser-failed','not-attempted'];
const summary={count:15,serializedFiles:2,errors:[],types:{AnimationClip:3},errorCount:0,warningCount:0,errorsTruncated:false,objectTableRows:15,parseStatuses:{'typed-complete':6,'typed-partial':6,'generic-raw':3}};
const asset={id:'example',name:'Example clip',type:'AnimationClip',pathId:'74',source:'fixture',bytes:'64',formats:['raw'],preview:'data',classId:74,parseStatus:'parser-failed',warning:'Reader stopped before the object boundary.'};

test('all locales have matching keys and localized labels for every parser status',()=>{
 const keys=Object.keys(locales.en).sort();
 for(const [lang,t] of Object.entries(locales)){
  assert.deepEqual(Object.keys(t).sort(),keys,lang);
  for(const status of statuses){assert.ok(parseStatusText(status,t));assert.notEqual(parseStatusText(status,t),status,`${lang}: ${status}`);}
  assert.ok(t.parseCoverageHint);assert.ok(t.diagnosticsTruncated);
 }
});

test('partial and generic support are not automatically load errors',()=>{
 assert.equal(hasLoadDiagnostics(summary),false);
 assert.equal(hasLoadDiagnostics({...summary,parseStatuses:{'parser-disabled':2,'not-attempted':1}}),false);
});

for(const [name,patch] of Object.entries({
 errorTotal:{errorCount:101},warningTotal:{warningCount:5},truncated:{errorsTruncated:true},
 parserFailure:{parseStatuses:{'parser-failed':1}},message:{errors:['An entry could not be loaded.']},
}))test(`load review remains visible for ${name}, independent of populated rows`,()=>{
 assert.ok(summary.count>0);assert.equal(hasLoadDiagnostics({...summary,...patch}),true);
});

test('legacy worker diagnostic messages remain visible without new counters',()=>{
 assert.equal(hasLoadDiagnostics({count:1,serializedFiles:1,types:{},errors:['Legacy diagnostic']}),true);
});

test('row tooltips retain name, original class, localized parser status, and warning',()=>{
 for(const t of Object.values(locales)){
  const title=assetDiagnosticTitle(asset,t);
  for(const text of [asset.name,asset.type,`${t.classId}: 74`,`${t.parseStatus}: ${t.parseFailed}`,asset.warning])assert.ok(title.includes(text));
 }
});

test('resource tooltips tolerate null parser metadata without claiming parsing success',()=>{
 const title=assetDiagnosticTitle({...asset,parseStatus:null,classId:null,warning:null},locales.en);
 assert.equal(title,'Example clip\nAnimationClip\nfixture');
 assert.ok(assetDiagnosticTitle({...asset,classId:0},locales.en).includes('Unity class ID: 0'));
});

test('unrecognized future status names remain legible',()=>{
 assert.equal(parseStatusText('future-reader-status',locales.en),'future-reader-status');
 assert.equal(parseStatusText('toString',locales.en),'toString');
});

test('source and remaining bytes are shown, including a zero remainder',()=>{
 for(const t of Object.values(locales)){
  assert.ok(assetDiagnosticTitle({...asset,remainingBytes:0},t).includes(`${t.remainingBytes}: 0`));
  assert.ok(assetDiagnosticTitle({...asset,remainingBytes:128},t).includes(`${t.remainingBytes}: 128`));
 }
});

test('UI wiring keeps scan, load, and logs separate; summary does not depend on empty rows',()=>{
 const ui=read('../src/main.tsx');
 assert.match(ui,/setScanDiagnostics\(result\.errors\)/);
 const readSkin=ui.slice(ui.indexOf('async function readSkin('),ui.indexOf('async function filter('));
 assert.match(readSkin,/setLoadDiagnostics\(data\.result\.errors\)/);
 assert.match(readSkin,/setLoadSummary\(data\.result\)/);
 assert.doesNotMatch(readSkin,/setScanDiagnostics/);
 assert.match(ui,/setLogText\(r\.text\|\|t\.noLogs\)/);
 assert.doesNotMatch(ui,/setDiagnostics/);
 assert.match(ui,/\{loadSummary&&<LoadDiagnostics result=\{loadSummary\} t=\{t\} onOpen=/);
 assert.equal((ui.match(/title=\{assetDiagnosticTitle\(a,t\)\}/g)||[]).length,2);
 assert.match(ui,/result\.errorsTruncated&&/);
});
