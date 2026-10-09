using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AssetStudio;
using Obj=AssetStudio.Object;

namespace Hok.Worker;
internal static partial class Program
{
 static string OriginalName(Obj obj)=>obj switch
 {
  MonoBehaviour mono=>mono.m_Name??"",
  NamedObject named=>named.m_Name??"",
  GameObject go=>go.m_Name??"",
  _=>obj.SchemaName??""
 };
 static object EvidenceIndex(JsonElement request)
 {
  if(manager is null)throw new InvalidOperationException("Load one DB first");
  if(manager.QtsLoadIsScoped)throw new InvalidOperationException("Scoped export loads cannot produce complete-corpus discovery evidence. Reload the complete DB first.");
  string output=Path.GetFullPath(request.GetProperty("output").GetString()!);
  string query=request.TryGetProperty("query",out var q)?q.GetString()??"":"";
  bool deep=request.TryGetProperty("deep",out var d)&&d.GetBoolean();
  if(manager.QtsDatabases.Count!=1)throw new InvalidOperationException("Evidence indexing accepts exactly one DB per invocation");
  var source=manager.QtsDatabases[0].Source;
  if(string.Equals(source,output,StringComparison.OrdinalIgnoreCase)||File.Exists(output))throw new IOException("Evidence output must be a new file, never the input");
  Directory.CreateDirectory(Path.GetDirectoryName(output)!);
  var info=new FileInfo(source);long size=info.Length;var stamp=info.LastWriteTimeUtc;
  string sourceSha;using(var input=File.OpenRead(source))sourceSha=Convert.ToHexString(SHA256.HashData(input));
  string partial=output+".partial";using var writer=new StreamWriter(new FileStream(partial,FileMode.CreateNew,FileAccess.Write,FileShare.Read),new UTF8Encoding(false),65536);
  void Write(object record)=>writer.WriteLine(JsonSerializer.Serialize(record,Json));
  Write(new{kind="source",schema=1,source,sha256=sourceSha,bytes=size,lastWriteUtc=stamp,query,deep,
   typeSchemaSources=manager.QtsTypeSchemaSources,identityPolicy="GUID-only same-DB and cross-DB matches are candidates, not confirmed GUID identities"});
  int count=0,refs=0,errors=0,hits=0;var statuses=new Dictionary<string,int>();
  foreach(var file in manager.assetsFileList)
  {
   Write(new{kind="serializedFile",source,identity=file.fullName,entry=file.containerEntryId,sfOffset=file.containerByteOffset,
    file.unityVersion,externals=file.m_Externals.Select((e,i)=>new{fileId=i+1,guid=e.guid.ToString(),guidBytes=Convert.ToHexString(e.guid.ToByteArray()),e.pathName,e.fileName,e.type}),
    types=file.m_Types.Select(t=>new{t.classID,t.m_ScriptTypeIndex,scriptId=t.m_ScriptID==null?null:Convert.ToHexString(t.m_ScriptID),typeHash=t.m_OldTypeHash==null?null:Convert.ToHexString(t.m_OldTypeHash)})});
   foreach(var meta in file.m_Objects)
   {
    CheckCancellation();file.ObjectsDic.TryGetValue(meta.m_PathID,out var original);Obj? obj=original;
    var problems=new List<string>();List<ReferenceObservation> links=[];var structuredStrings=new List<object>();int omittedStringFields=0;
    try
    {
     // Material references are needed for the global dependency graph; decoding
     // a Material does not decode its textures. Other heavy payloads remain
     // explicitly deferred until a targeted deep pass.
     if(obj is DeferredObject && (deep||obj.type==ClassIDType.Material))obj=DeferredObject.Resolve(obj);
     if(obj!=null)links=ReferenceCollector.Collect(obj,problems,(field,value,offset)=>{
      bool matches=query.Length>0&&value.Contains(query,StringComparison.OrdinalIgnoreCase);
      if(matches)hits++;
      if(value.Length>0&&(matches||value.Contains('/')||value.Contains('\\'))){
       if(value.Length>4096){omittedStringFields++;return;}
       structuredStrings.Add(new{field,value,serializedOffset=offset,objectOffset=offset-meta.byteStart,payloadOffset=file.containerByteOffset+offset,
        utf8Bytes=Encoding.UTF8.GetByteCount(value),evidence="validated-type-tree-string; logical path candidate, not a confirmed object binding"});
      }
     });
    }catch(Exception e){problems.Add(e.GetBaseException().Message);errors++;}
    file.ParseStatuses.TryGetValue(meta.m_PathID,out var state);
    string? rawHash=null;
    try{using var reader=new ObjectReader(file.reader,file,meta,manager.Game);reader.Reset();rawHash=Convert.ToHexString(SHA256.HashData(reader.BaseStream));}
    catch(Exception e){problems.Add("Raw object range: "+e.GetBaseException().Message);errors++;}
    string name=obj==null?"":OriginalName(obj),id=Hash(file.fullName)+":"+meta.m_PathID;
    if(query.Length>0&&name.Contains(query,StringComparison.OrdinalIgnoreCase))hits++;
    var records=links.Select(link=>{
     var external=link.FileId>0&&link.FileId<=file.m_Externals.Count?file.m_Externals[link.FileId-1]:null;
     string? rawHex=null;
     if(link.SerializedOffset is long at && at>=meta.byteStart && at<=meta.byteStart+meta.byteSize-12)
     {file.reader.Position=at;rawHex=Convert.ToHexString(file.reader.ReadBytes(12));}
     return new{link.Field,link.FileId,pathId=link.PathId.ToString(),link.ExpectedType,link.Origin,
      serializedOffset=link.SerializedOffset,objectOffset=link.ObjectOffset,
      payloadOffset=link.SerializedOffset is long offset?file.containerByteOffset+offset:(long?)null,rawHex,
      guid=external?.guid.ToString(),guidBytes=external==null?null:Convert.ToHexString(external.guid.ToByteArray()),externalPath=external?.pathName,
      status=link.Resolution.Status,reason=link.Resolution.Reason,link.Resolution.IdentityConfirmed,target=link.Resolution.TargetIdentity};
    }).ToArray();
    refs+=records.Length;count++;string status=state?.Status??"not-attempted";statuses[status]=statuses.GetValueOrDefault(status)+1;
    object? script=obj is MonoBehaviour mono?new{mono.m_Script.m_FileID,pathId=mono.m_Script.m_PathID.ToString(),
       status=mono.m_Script.ResolveEvidence().Status}:null;
    object? stream=null;
    try{if(obj!=null&&GetStream(obj) is{} s)stream=new{path=s.Path,qtsId=QtsVFSFile.Compute(s.Path,true).ToString(),s.Offset,s.Size,s.Kind};}
    catch(Exception e){problems.Add("Resource stream metadata: "+e.GetBaseException().Message);errors++;}
    Write(new{kind="object",id,source,sourceSha256=sourceSha,entry=file.containerEntryId,sfOffset=file.containerByteOffset,sf=file.fullName,
     pathId=meta.m_PathID.ToString(),classId=meta.classID,type=ObjectParseStatus.ClassName(meta.classID,meta.serializedType),name,
     nameOrigin=name.Length==0?"unnamed":"serialized-field",byteStart=meta.byteStart,payloadOffset=file.containerByteOffset+meta.byteStart,bytes=meta.byteSize,
     sha256=rawHash,parseStatus=status,consumed=state?.ConsumedBytes,remaining=state?.RemainingBytes,parseError=state?.Error,
     typeHash=meta.serializedType?.m_OldTypeHash==null?null:Convert.ToHexString(meta.serializedType.m_OldTypeHash),
     scriptId=meta.serializedType?.m_ScriptID==null?null:Convert.ToHexString(meta.serializedType.m_ScriptID),
     formats=obj==null?new[]{"raw"}:Exporters.Formats(obj),script,stream,
     scriptMetadata=obj is MonoScript ms?new{ms.m_ClassName,ms.m_Namespace,ms.m_AssemblyName,ms.m_ExecutionOrder,propertiesHash=ms.m_PropertiesHash==null?null:Convert.ToHexString(ms.m_PropertiesHash),ms.m_LegacyPropertiesHash}:null,
     referenceCoverage=obj is DeferredObject?"deferred":"parsed-fields-and-validated-type-tree",
     references=records,structuredStrings,omittedStringFields,stringIndexPolicy="Nonempty path-like or query-matching fields up to 4096 characters; larger matching fields counted as omitted and retained in full object export",issues=problems});
    if(original is DeferredObject deferred)deferred.ReleaseDecodedValue();
    if(count%10000==0){writer.Flush();Console.Error.WriteLine("Evidence objects "+count);}
   }
  }
  foreach(var entry in manager.QtsDatabases[0].Entries)
  {
   CheckCancellation();var matches=query.Length>0&&entry.Complete?FindByteHints(entry,query):[];
   hits+=matches.Count;
   Write(new{kind="entry",source,id=entry.FileId.ToString(),payloadKind=entry.Kind,entry.ParseStatus,entry.Complete,entry.PayloadBytes,entry.ExpectedBytes,
    entry.ObjectCount,entry.AssetTypes,entry.Error,queryHints=matches,hintsAreReferences=false});
  }
  info.Refresh();if(info.Length!=size||info.LastWriteTimeUtc!=stamp)throw new IOException("Source changed during scan");
  int entryIssues=manager.QtsDatabases[0].Entries.Count(e=>!e.Complete||e.Error!=null||e.ParseStatus is "parser-failed" or "object-table-partial" or "not-attempted");
  Write(new{kind="summary",objects=count,references=refs,errors,hits,statuses,entries=manager.QtsDatabases[0].Entries.Count,entryIssues,semanticComplete=false});
  writer.Flush();writer.Dispose();File.Move(partial,output);
  return new{output,objects=count,references=refs,errors,hits,statuses,sourceSha256=sourceSha,entryIssues,semanticComplete=false};
 }
 static List<object> FindByteHints(QtsEntryRecord entry,string query)
 {
  // Text hits are discovery hints only, never promoted to PPtr/resource links.
  var result=new List<object>();using var input=entry.Open();
  var needles=new[]{Encoding.UTF8.GetBytes(query),Encoding.Unicode.GetBytes(query)};
  const int overlap=256;var buffer=new byte[65536+overlap];int carry=0;long baseOffset=0;
  while(true)
  {
   int got=input.Read(buffer,carry,buffer.Length-carry);if(got==0)break;int length=carry+got;
   for(int n=0;n<needles.Length;n++)
   {
    int begin=0;
    while(begin<=length-needles[n].Length)
    {
     int found=buffer.AsSpan(begin,length-begin).IndexOf(needles[n]);if(found<0)break;int at=begin+found;begin=at+needles[n].Length;
     if(at+needles[n].Length<=carry)continue;
     int start=Math.Max(0,at-48),end=Math.Min(length,at+160);
     result.Add(new{offset=baseOffset+at,encoding=n==0?"utf8":"utf16le",context=Encoding.UTF8.GetString(buffer,start,end-start).Replace('\0',' '),evidence="byte-string-candidate"});
     if(result.Count==256){result.Add(new{truncated=true,reason="256 hints per entry limit; original payload retained"});return result;}
    }
   }
   carry=Math.Min(overlap,length);Array.Copy(buffer,length-carry,buffer,0,carry);baseOffset+=length-carry;
  }
  return result;
 }
}
