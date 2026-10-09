using System.Collections;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AssetStudio;
using Obj=AssetStudio.Object;

var root=Path.GetFullPath(args[0]);var folder=Path.GetFullPath(args[1]);
var output=Path.Combine(root,"planning/mapping-audit-report.json");
var inputs=Directory.GetFiles(folder).Order().Select(p=>new{path=p,name=Path.GetFileName(p),bytes=new FileInfo(p).Length,sha256=Hash(File.ReadAllBytes(p))}).ToArray();
var snapshot=Path.Combine(root,".cache/mapping-audit-inputs",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(snapshot);
foreach(var input in inputs){var dest=Path.Combine(snapshot,input.name);File.Copy(input.path,dest,false);File.SetAttributes(dest,FileAttributes.ReadOnly);}
var paths=inputs.Where(f=>f.name.EndsWith(".db",StringComparison.OrdinalIgnoreCase)).Select(f=>f.path).ToArray();
var qts=new List<object>();var entryIds=new HashSet<string>();var memberships=new Dictionary<string,List<string>>();
foreach(var path in paths){
 using var reader=new FileReader(path);reader.Endian=EndianType.LittleEndian;reader.Position=0;var header=new QtsVFSFile.FHoKHeader(reader);var index=new QtsVFSFile(reader);
 var entries=index.Entries.Select(e=>new{id=e.Key.ToString(),chunks=e.Value.Select(c=>new{offset=c.Offset,compressed=c.CompressedSize,uncompressed=c.UncompressedSize,main=c.MainBlock,sub=c.SubBlock}).ToArray()}).ToArray();
 foreach(var e in index.Entries){var id=e.Key.ToString();entryIds.Add(id);if(!memberships.ContainsKey(id))memberships[id]=[];memberships[id].Add(Path.GetFileName(path)+(e.Value.Any(c=>c.UncompressedSize>0)?":payload":":metadata-only"));}
 qts.Add(new{file=Path.GetFileName(path),header=new{magic=header.Magic.ToString("X16"),header.Size,header.FullSize,index=header.Index,indexData=header.IndexData,entries1=header.Entries1,entries2=header.Entries2,entries3=header.Entries3},entries});
}
var logger=new AuditLogger();Logger.Default=logger;Logger.Flags=LoggerEvent.Error|LoggerEvent.Warning;
var manager=new AssetsManager{Game=GameManager.GetGame(GameType.HonorOfKings)};manager.LoadFilesReadOnly(paths);
var objects=manager.assetsFileList.SelectMany(f=>f.Objects).ToArray();
var files=manager.assetsFileList.Select(f=>new{
 id=f.fileName,objects=f.m_Objects.Count,parsed=f.Objects.Count,version=f.unityVersion,format=f.header.m_Version.ToString(),f.userInformation,
 typeTrees=f.m_Types.Count(t=>t.m_Type?.m_Nodes?.Count>0),
 types=f.m_Types.Select(t=>new{classId=t.classID,name=((ClassIDType)t.classID).ToString(),treeNodes=t.m_Type?.m_Nodes?.Count??0,scriptIndex=t.m_ScriptTypeIndex,scriptId=t.m_ScriptID is null?null:Convert.ToHexString(t.m_ScriptID),typeHash=t.m_OldTypeHash is null?null:Convert.ToHexString(t.m_OldTypeHash),t.m_KlassName,t.m_NameSpace,t.m_AsmName}).ToArray(),
 externals=f.m_Externals.Select((e,i)=>new{fileId=i+1,e.guid,e.type,e.pathName,e.fileName,loaded=manager.assetsFileList.Any(x=>x.fileName.Equals(e.fileName,StringComparison.OrdinalIgnoreCase))}).ToArray()
}).ToArray();
var pointers=new List<Pointer>();
foreach(var obj in objects)Walk(obj,obj,"",new HashSet<object>(ReferenceEqualityComparer.Instance),0,pointers,manager);
var monos=objects.OfType<MonoBehaviour>().Select(m=>new{
 file=m.assetsFile.fileName,pathId=m.m_PathID.ToString(),name=m.m_Name,bytes=m.byteSize,scriptFileId=m.m_Script.m_FileID,scriptPathId=m.m_Script.m_PathID.ToString(),
 owner=m.m_GameObject.TryGet(out var go)?go.Name:null,scriptResolved=m.m_Script.TryGet(out _),
 typeTreeNodes=m.serializedType.m_Type?.m_Nodes?.Count??0,strings=Strings(m.GetRawData()).ToArray()
}).ToArray();
var textureMappings=objects.OfType<Texture2D>().Select(t=>{
 var s=t.m_StreamData;var id=string.IsNullOrEmpty(s?.path)?null:QtsVFSFile.Compute(s.path,true).ToString();
 var entry=manager.ContainerEntries.FirstOrDefault(e=>e.Id==id);
 return new{file=t.assetsFile.fileName,pathId=t.m_PathID.ToString(),name=t.Name,format=t.m_TextureFormat.ToString(),path=s?.path,offset=s?.offset,bytes=s?.size,resourceId=id,exists=entry is not null,wholeStream=entry is not null&&s!.offset==0&&s.size==entry.Data.Length,hash=entry is null?null:Hash(entry.Data)};
}).ToArray();
var candidatePattern=new Regex("manifest|catalog|mapping|index|preload|depend|event|action|skill|prefab|assets/|resources/|lianpo|10500|\\.(mat|anim|shader|controller|resS|asset|json|xml|bytes|txt)$",RegexOptions.IgnoreCase);
var entriesReport=manager.ContainerEntries.Select(e=>new{
 id=e.Id,source=Path.GetFileName(e.Source),kind=e.Kind,bytes=e.Data.Length,sha256=Hash(e.Data),head=Convert.ToHexString(e.Data.AsSpan(0,Math.Min(40,e.Data.Length))),
 textureNames=textureMappings.Where(t=>t.resourceId==e.Id).Select(t=>t.name).ToArray(),
 strings=e.Kind=="AssetsFile"?Strings(e.Data).Where(s=>candidatePattern.IsMatch(s)).Distinct().Take(1500).ToArray():e.Data.Length<128?Strings(e.Data).ToArray():Array.Empty<string>()
}).ToArray();
var roots=objects.OfType<Transform>().Where(t=>t.m_Father.IsNull).Select(t=>new{file=t.assetsFile.fileName,pathId=t.m_PathID.ToString(),gameObject=t.m_GameObject.TryGet(out var go)?go.Name:null,children=t.m_Children.Count}).ToArray();
var named=objects.Where(o=>!string.IsNullOrWhiteSpace(o.Name)).Select(o=>new{file=o.assetsFile.fileName,pathId=o.m_PathID.ToString(),name=o.Name,type=o.type.ToString()}).ToArray();
var inventory=objects.Select(o=>new{file=o.assetsFile.fileName,pathId=o.m_PathID.ToString(),name=o.Name,type=o.type.ToString(),runtimeClass=o.GetType().Name,bytes=o.byteSize}).ToArray();
var generic=objects.Where(o=>o.GetType()==typeof(Obj)).Select(o=>new{file=o.assetsFile.fileName,pathId=o.m_PathID.ToString(),type=o.type.ToString(),bytes=o.byteSize,strings=Strings(o.GetRawData()).Distinct().ToArray()}).ToArray();
var record=inputs.FirstOrDefault(i=>i.name=="record.bytes");var recordBytes=record is null?[]:File.ReadAllBytes(record.path);
manager.Clear();
bool unchanged=inputs.All(i=>Hash(File.ReadAllBytes(i.path))==i.sha256);
if(!unchanged)throw new IOException("Source input changed during audit.");
var report=new{
 date=DateTimeOffset.UtcNow,scope="Read-only complete 3200010500 folder audit; no replacement, rebuild, application-code edits or package writes.",folder,inputs,snapshot,inputHashesUnchanged=unchanged,
 counts=new{inputFiles=inputs.Length,dbFiles=paths.Length,indexedFileIds=entryIds.Count,decodedEntries=entriesReport.Length,serializedFiles=files.Length,declaredObjects=files.Sum(f=>f.objects),objects=objects.Length,types=objects.GroupBy(o=>o.type.ToString()).ToDictionary(g=>g.Key,g=>g.Count()),parsedPublicPointers=pointers.Count,nonNullPointers=pointers.Count(p=>p.targetPathId!="0"),unresolvedNonNull=pointers.Count(p=>p.targetPathId!="0"&&!p.resolved)},
 record=new{bytes=recordBytes.Length,hex=Convert.ToHexString(recordBytes),strings=Strings(recordBytes).ToArray()},
 qts,entryMembership=memberships,files,entries=entriesReport,textureMappings,roots,objectInventory=inventory,namedObjects=named,monoBehaviours=monos,genericObjects=generic,pointers,errors=logger.Errors,
 limitations=new[]{"No known-working modified reference or donor was supplied; no game-runtime compatibility claim.","Pointer traversal covers parsed public fields only; absent type trees and fields discarded by legacy parsers prevent a complete reference/call graph.","ASCII strings are leads, not proof of runtime call semantics.","Opaque texture streams are assigned through actual StreamingInfo path hashes, not text search."}
};
File.WriteAllText(output,JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true,IncludeFields=true}));
Console.WriteLine(JsonSerializer.Serialize(new{output,report.counts,genericObjectCount=generic.Length,errors=logger.Errors,inputHashesUnchanged=unchanged},new JsonSerializerOptions{IncludeFields=true}));

static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
static IEnumerable<string> Strings(byte[] bytes){
 int start=-1;for(int i=0;i<=bytes.Length;i++){bool printable=i<bytes.Length&&bytes[i]>=32&&bytes[i]<=126;if(printable&&start<0)start=i;if(!printable&&start>=0){if(i-start>=5)yield return Encoding.ASCII.GetString(bytes,start,i-start);start=-1;}}
}
static void Walk(object? value,Obj owner,string field,HashSet<object> seen,int depth,List<Pointer> rows,AssetsManager manager){
 if(value is null||depth>18)return;var type=value.GetType();
 if(type.IsPrimitive||type.IsEnum||value is string||value is decimal||value is byte[])return;
 if(!type.IsValueType&&!seen.Add(value))return;
 if(type.IsGenericType&&type.GetGenericTypeDefinition()==typeof(PPtr<>)){
  int fileId=(int)type.GetField("m_FileID")!.GetValue(value)!;long pathId=(long)type.GetField("m_PathID")!.GetValue(value)!;
  var external=fileId>0&&fileId<=owner.assetsFile.m_Externals.Count?owner.assetsFile.m_Externals[fileId-1]:null;
  var target=fileId==0?owner.assetsFile:external is null?null:manager.assetsFileList.FirstOrDefault(f=>f.fileName.Equals(external.fileName,StringComparison.OrdinalIgnoreCase));
  Obj? obj=null;bool found=target is not null&&target.ObjectsDic.TryGetValue(pathId,out obj);
  rows.Add(new(owner.assetsFile.fileName,owner.m_PathID.ToString(),owner.type.ToString(),owner.Name,field,fileId,pathId.ToString(),external?.pathName,external?.guid.ToString(),target?.fileName,found,obj?.type.ToString(),obj?.Name));return;
 }
 if(value is IEnumerable sequence){int i=0;foreach(var item in sequence){Walk(item,owner,field+"["+(i++)+"]",seen,depth+1,rows,manager);}return;}
 if(type.Namespace is null||(!type.Namespace.StartsWith("AssetStudio")&&!type.Namespace.StartsWith("System.Collections.Generic")))return;
 foreach(var member in type.GetFields(BindingFlags.Public|BindingFlags.Instance)){
  if(member.Name is "assetsFile" or "reader" or "serializedType" or "version" or "assetsManager")continue;
  var next=member.GetValue(value);if(next is Obj&&next!=owner)continue;
  Walk(next,owner,field.Length==0?member.Name:field+"."+member.Name,seen,depth+1,rows,manager);
 }
}
record Pointer(string sourceFile,string sourcePathId,string sourceType,string sourceName,string field,int targetFileId,string targetPathId,string? externalPath,string? externalGuid,string? targetFile,bool resolved,string? targetType,string? targetName);
sealed class AuditLogger:ILogger{public List<string> Errors{get;}=[];public void Log(LoggerEvent level,string message){if(level is LoggerEvent.Error or LoggerEvent.Warning)Errors.Add(message);}}
