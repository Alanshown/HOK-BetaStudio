const crypto=require('node:crypto');
const hash=value=>crypto.createHash('sha256').update(JSON.stringify(value)).digest('hex');

// A successful old export is reusable only for the same input and load context.
// Parser DLLs alone do not identify a changed DB, schema or expanded hierarchy.
function context({parserRevision,sourceSha256,schemaSha256,dependencies,selectedEntries}){
 return hash({version:1,parserRevision,sourceSha256:sourceSha256.toLowerCase(),schemaSha256,
  dependencies:[...dependencies].sort((a,b)=>a.path.localeCompare(b.path)),
  selectedEntries:selectedEntries===null?null:[...new Set(selectedEntries)].sort()});
}
function reusable(previous,currentContext){return previous?.ok===true&&!previous.rawFallback&&previous.exportContext===currentContext;}
module.exports={context,reusable};
