import type {Asset,LoadSummary} from './types';
import type {Text} from './i18n';

const statusLabels={
 'typed-complete':'parseTypedComplete',
 'typed-partial':'parseTypedPartial',
 'generic-raw':'parseGenericRaw',
 'parser-disabled':'parseDisabled',
 'parser-failed':'parseFailed',
 'not-attempted':'parseNotAttempted',
} as const;

export function parseStatusText(status:string,t:Text):string{
 const key=Object.hasOwn(statusLabels,status)?statusLabels[status as keyof typeof statusLabels]:undefined;
 return key?t[key]:status;
}

export function hasLoadDiagnostics(result:LoadSummary):boolean{
 return result.errors.length>0||(result.errorCount??0)>0||(result.warningCount??0)>0||Boolean(result.errorsTruncated)||(result.parseStatuses?.['parser-failed']??0)>0;
}

export function assetDiagnosticTitle(asset:Asset,t:Text):string{
 return [asset.name,asset.type,asset.source,asset.classId==null?null:`${t.classId}: ${asset.classId}`,asset.parseStatus?`${t.parseStatus}: ${parseStatusText(asset.parseStatus,t)}`:null,asset.remainingBytes==null?null:`${t.remainingBytes}: ${asset.remainingBytes}`,asset.warning].filter(value=>value!=null&&value!=='').join('\n');
}
