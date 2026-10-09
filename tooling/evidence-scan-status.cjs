function scanStatus(report){
 const readErrors=report.readErrors??((report.load?.errorCount??0)+(report.indexed?.errors??0)+(report.indexed?.entryIssues??0));
 // Deferred payloads are intentional lazy reads, not parser failures. Every
 // other noncomplete state must stay visible even with an empty error logger.
 const incompleteObjects=Object.entries(report.indexed?.statuses??{}).reduce((sum,[status,count])=>sum+(status==='typed-complete'||status==='deferred'&&report.deep!==true?0:count),0);
 return {ok:report.ok===true&&readErrors===0&&incompleteObjects===0,rpcCompleted:report.ok===true,readErrors,incompleteObjects};
}
module.exports={scanStatus};
