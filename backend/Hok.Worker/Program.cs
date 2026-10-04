using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AssetStudio;
using Hok.Contracts;
using Obj=AssetStudio.Object;
namespace Hok.Worker;
internal sealed record AssetRow(string Id,string Name,string Type,string PathId,string Source,string Bytes,string[] Formats,string Preview,int? ClassId=null,string? ParseStatus=null,string? Warning=null,long? ConsumedBytes=null,long? RemainingBytes=null);
internal sealed class CaptureLog:ILogger {
 public readonly List<string> Errors=[];
 public int ErrorCount { get; private set; }
 public int WarningCount { get; private set; }
 public bool ErrorsTruncated => ErrorCount + WarningCount > Errors.Count;
 public void AddError(string message) => Log(LoggerEvent.Error,message);
 public void Log(LoggerEvent level,string message){Console.Error.WriteLine($"[{level}] {message}");if(level==LoggerEvent.Error)ErrorCount++;if(level==LoggerEvent.Warning)WarningCount++;if(level is LoggerEvent.Error or LoggerEvent.Warning && Errors.Count<100)Errors.Add(message);}
}
internal static class Program {
 internal static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
 static readonly Dictionary<string,Obj> Objects=[];
 static readonly Dictionary<string,ResourceAsset> Resources=[];
 static readonly List<AssetRow> Rows=[];
 static AssetsManager? manager;static string cache="";static ReplacementSession? replacements;
 static int Main(string[] args) {
  Console.InputEncoding=new UTF8Encoding(false);Console.OutputEncoding=new UTF8Encoding(false);
  CultureInfo.CurrentCulture=CultureInfo.InvariantCulture;
  cache=args.Length>0?Path.GetFullPath(args[0]):Path.Combine(Path.GetTempPath(),"hok-preview-"+Guid.NewGuid());
  Directory.CreateDirectory(cache);AudioTools.Scratch=cache;
  Logger.Flags=LoggerEvent.Info|LoggerEvent.Warning|LoggerEvent.Error;
  while(Console.ReadLine() is { } line) {string id="";try{
   if(line.Length>2_000_000)throw new InvalidDataException("Request too large");
   using var doc=JsonDocument.Parse(line);var req=doc.RootElement;id=req.GetProperty("id").GetString()!;
   var data=Dispatch(req.GetProperty("method").GetString()!,req.GetProperty("payload"));
   Console.WriteLine(JsonSerializer.Serialize(new{id,ok=true,data},Json));
  }catch(Exception e){Console.Error.WriteLine(e);Console.WriteLine(JsonSerializer.Serialize(new{id,ok=false,error=new{code="WORKER_ERROR",message=e.GetBaseException().Message}},Json));}}
  manager?.Clear();return 0;
 }
 static object Dispatch(string method,JsonElement p)=>method switch {
  "load"=>Load(p.GetProperty("paths").EnumerateArray().Select(x=>x.GetString()!).ToArray()),
  "list"=>List(p),"neighbors"=>Neighbors(p.GetProperty("assetId").GetString()!),
  "bank"=>Bank(p.GetProperty("assetId").GetString()!),
  "preview"=>Preview(p.GetProperty("assetId").GetString()!),"export"=>Export(p),
  "replacementState"=>replacements?.State()??throw new InvalidOperationException("Load a package first"),
  "replace"=>replacements?.Stage(p.GetProperty("assetId").GetString()!,p.GetProperty("path").GetString()!)??throw new InvalidOperationException("Load a package first"),
  "rebuild"=>replacements?.Build(p.GetProperty("output").GetString()!)??throw new InvalidOperationException("Load a package first"),
  "ping"=>new{version="1.3",language="C#"},"dump"=>Dump(p.GetProperty("assetId").GetString()!),
  _=>throw new InvalidOperationException("Unknown method")};
 static string Hash(string text)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToUpperInvariant())))[..16];
 static void AddResource(ResourceAsset item){
  if(!Resources.TryAdd(item.Id,item))return;
  Rows.Add(new(item.Id,item.Name,item.Type,item.Id.Split('/').Last(),item.Source,item.Data.Length.ToString(),item.Formats,item.Preview));
 }
 static object Load(string[] paths) {
  replacements=null;manager?.Clear();Objects.Clear();Resources.Clear();Rows.Clear();var log=new CaptureLog();Logger.Default=log;
  manager=new AssetsManager{Game=GameManager.GetGame(GameType.HonorOfKings)};
  var accepted=new List<string>();
  foreach(var path in paths){if(!File.Exists(path))throw new FileNotFoundException(path);if(new FileInfo(path).Length<32){log.AddError(Path.GetFileName(path)+": truncated file header");continue;}accepted.Add(path);}
  if(accepted.Count>0)manager.LoadFilesReadOnly(accepted.ToArray());
  foreach(var file in manager.assetsFileList){
   string prefix=Hash(file.fullName);
   foreach(var meta in file.m_Objects){
    file.ObjectsDic.TryGetValue(meta.m_PathID,out var obj);
    file.ParseStatuses.TryGetValue(meta.m_PathID,out var parse);
    string type=ObjectParseStatus.ClassName(meta.classID),status=parse?.Status??"not-attempted";
    string? warning=status=="typed-complete"?null:$"{status}: {parse?.Error??"Incomplete semantic interpretation; original raw bytes remain available."}";
    var id=$"{prefix}:{meta.m_PathID}";
    if(obj is not null){
     Objects.Add(id,obj);
     Rows.Add(new(id,string.IsNullOrWhiteSpace(obj.Name)?$"{type} #{meta.m_PathID}":obj.Name,type,meta.m_PathID.ToString(),file.fullName,meta.byteSize.ToString(),Exporters.Formats(obj),obj switch{Texture2D or Sprite=>"image",Mesh=>"model",AnimationClip=>"animation",AudioClip=>"audio",_=>"data"},meta.classID,status,warning,parse?.ConsumedBytes,parse?.RemainingBytes));
    }else{
     byte[] bytes=[];bool rawAvailable=false;long position=file.reader.Position;
     try{
      if(meta.byteSize>512*1024*1024||meta.byteStart<0||meta.byteStart>file.reader.Length-meta.byteSize)throw new InvalidDataException("Invalid/oversized raw object range; inspect original SerializedFile container.");
      file.reader.Position=meta.byteStart;bytes=file.reader.ReadBytes((int)meta.byteSize);rawAvailable=bytes.Length==meta.byteSize;
      if(!rawAvailable)throw new EndOfStreamException("Short raw object read");
     }catch(Exception e){warning+=" "+e.Message;log.AddError($"{file.fullName} object {meta.m_PathID}: {e.Message}");}
     finally{file.reader.Position=position;}
     var resource=new ResourceAsset(id,$"{type} #{meta.m_PathID}",file.fileName,type,"bin",bytes,Warning:warning,RawAvailable:rawAvailable,DeclaredBytes:meta.byteSize);
     Resources.Add(id,resource);
     Rows.Add(new(id,resource.Name,type,meta.m_PathID.ToString(),file.fullName,meta.byteSize.ToString(),resource.Formats,resource.Preview,meta.classID,status,warning,parse?.ConsumedBytes,parse?.RemainingBytes));
    }
   }
  }
  var capturedNames=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  foreach(var entry in manager.ContainerEntries){
   capturedNames.Add(Path.GetFullPath(entry.Source)+"|"+entry.Id.Replace('\\','/'));var detected=ResourceAsset.Detect(entry.Data,entry.Kind);
   AddResource(new($"entry:{Hash(entry.Source)}/{entry.Id}",entry.Id+"."+detected.extension,entry.Source,detected.type,detected.extension,entry.Data,Warning:entry.Warning));
  }
  foreach(var pair in manager.ResourceFiles.Where(p=>!capturedNames.Contains(p.Key))){
   var stream=pair.Value.BaseStream;long position=stream.Position;
   try{if(stream.Length>512L*1024*1024){log.AddError(pair.Key+": resource exceeds safe in-memory limit");continue;}
    stream.Position=0;var bytes=pair.Value.ReadBytes((int)stream.Length);var detected=ResourceAsset.Detect(bytes,"");
    AddResource(new("resource:"+Hash(string.Join("|",paths))+"/"+Hash(pair.Key),pair.Key+"."+detected.extension,pair.Key,detected.type,detected.extension,bytes));
   }finally{stream.Position=position;}
  }
  foreach(var parent in Resources.Values.Where(r=>r.Extension is "pck" or "bnk").ToArray()){
   try{foreach(var child in WwiseIndex.Expand(parent))AddResource(child);}
   catch(Exception e){log.AddError(parent.Name+": "+e.Message+"; the original container remains exportable.");}
  }
  // Editing candidates must not live in the WebView's publicly mapped preview directory.
  var editCache=Path.Combine(Path.GetDirectoryName(cache)!,"editing",Path.GetFileName(cache));
  replacements=new ReplacementSession(manager,Objects,Resources,accepted.ToArray(),editCache);
  return new{count=Rows.Count,unityObjects=Objects.Count,containerFiles=manager.ContainerEntries.Count,serializedFiles=manager.assetsFileList.Count,errors=log.Errors,errorCount=log.ErrorCount,warningCount=log.WarningCount,errorsTruncated=log.ErrorsTruncated,
   objectTableRows=manager.assetsFileList.Sum(f=>f.m_Objects.Count),parseStatuses=manager.assetsFileList.SelectMany(f=>f.ParseStatuses.Values).GroupBy(s=>s.Status).ToDictionary(g=>g.Key,g=>g.Count()),loadedFiles=accepted.ToArray(),types=Rows.GroupBy(x=>x.Type).ToDictionary(g=>g.Key,g=>g.Count())};
 }
 static object List(JsonElement p) {
  var query=p.TryGetProperty("query",out var q)?q.GetString()??"":"";
  var type=p.TryGetProperty("type",out var t)?t.GetString()??"":"";
  int page=p.TryGetProperty("page",out var a)?Math.Max(0,a.GetInt32()):0;
  var rows=Rows.Where(x=>(type==""||x.Type==type)&&(x.Name.Contains(query,StringComparison.OrdinalIgnoreCase)||x.PathId.Contains(query)||x.Source.Contains(query,StringComparison.OrdinalIgnoreCase))).ToList();
  return new{total=rows.Count,page,items=rows.Skip(checked(page*200)).Take(200).ToArray()};
 }
 static AssetRow Row(string id)=>Rows.FirstOrDefault(r=>r.Id==id)??throw new InvalidOperationException("Asset no longer loaded");
 static object Neighbors(string id){
  var current=Row(id);var list=Rows.Where(r=>current.Preview is "audio" or "image" or "model"?r.Preview==current.Preview:r.Type==current.Type).ToList();
  int index=list.FindIndex(r=>r.Id==id);return new{index=index+1,total=list.Count,previous=index>0?list[index-1]:null,next=index+1<list.Count?list[index+1]:null};
 }
 static object Dump(string id) {
  string text=Resources.TryGetValue(id,out var resource)?JsonSerializer.Serialize(resource.Describe(),new JsonSerializerOptions(Json){WriteIndented=true}):Objects.TryGetValue(id,out var obj)?obj.Dump()??Exporters.ToJson(obj):throw new InvalidOperationException("Asset no longer loaded");
  var row=Row(id);if(row.ParseStatus is not null)text=$"Class ID: {row.ClassId}; PathID: {row.PathId}; parse status: {row.ParseStatus}\nSource: {row.Source}\nConsumed bytes: {row.ConsumedBytes}; remaining bytes: {row.RemainingBytes}\n{row.Warning}\n\n"+text;
  return new{text=text.Length>262144?text[..262144]+"\n[TRUNCATED: preview limited to 262144 characters; export JSON/raw for complete data.]":text,truncated=text.Length>262144};
 }
 static object Bank(string id){
  if(!Resources.TryGetValue(id,out var bank)||!bank.IsBank)throw new InvalidOperationException("SoundBank no longer loaded");
  var items=BankArchive.Audio(bank).Select(r=>Row(r.Id)).ToArray();
  return new{bank=Row(id),items,total=items.Length};
 }
 static object Preview(string id) {
  if(Objects.TryGetValue(id,out var animation)&&animation is AnimationClip clip)return AnimationPreview.Build(manager!,clip,cache);
  var row=Row(id);var format=row.Preview switch{"audio"=>"wav","model"=>"obj","image"=>Resources.TryGetValue(id,out var image)?image.Extension:"png",_=>"json"};
  var file=Path.Combine(cache,Guid.NewGuid().ToString("N")+"."+format);
  if(Resources.TryGetValue(id,out var resource)){
   if(row.Preview=="image")File.WriteAllBytes(file,resource.Data);
   else resource.Write(format,file);
  }else Exporters.Write(Objects[id],format,file,new ExportOptions());
  return new{file=Path.GetFileName(file),kind=format,size=new FileInfo(file).Length};
 }
 static object Export(JsonElement p) {
  string output=Path.GetFullPath(p.GetProperty("output").GetString()!);Directory.CreateDirectory(output);
  var ids=p.GetProperty("assetIds").EnumerateArray().Select(x=>x.GetString()!).Distinct().ToArray();if(ids.Length==0)throw new InvalidOperationException("No selection");
  var format=p.GetProperty("format").GetString()!;var options=p.TryGetProperty("options",out var o)?o.Deserialize<ExportOptions>(Json)??new():new();
  var results=new List<object>();int success=0,rawFallback=0;
  foreach(var id in ids){
   try{
    var row=Row(id);var chosen=format=="auto"?row.Formats.First():format;
    if(!row.Formats.Contains(chosen))throw new NotSupportedException("Format unavailable: "+chosen);
    string suffix=Hash(id)[..8];var name=Identity.SafeName(row.Name)+"_"+suffix;
    var itemFolder=Path.Combine(output,name);int count=1;while(Directory.Exists(itemFolder)||File.Exists(itemFolder))itemFolder=Path.Combine(output,name+"_"+count++);
    Directory.CreateDirectory(itemFolder);var ext=Resources.TryGetValue(id,out var resource)?resource.ExportExtension(chosen):Exporters.Extension(Objects[id],chosen);
    var filename=resource?.IsBank==true?Identity.SafeName(Path.GetFileNameWithoutExtension(resource.Name))+(chosen.StartsWith("zip-",StringComparison.Ordinal)?"-"+chosen[4..]:"")+"."+ext:name+"."+ext;
    var dest=Path.Combine(itemFolder,filename);
    void Write(string requested,string target){if(resource is not null)resource.Write(requested,target);else Exporters.Write(Objects[id],requested,target,options);}
    string? warning=null;string requestedFormat=chosen;
    try{Write(chosen,dest);}
    catch(Exception conversion) when(format=="auto" && chosen!="raw" && row.Formats.Contains("raw")){
     // Never call a failed semantic conversion successful. Auto mode can still
     // preserve the original bytes, with an explicit fallback in UI and report.
     if(File.Exists(dest))File.Delete(dest);
     warning=conversion.GetBaseException().Message;
     chosen="raw";ext=resource is not null?resource.ExportExtension(chosen):Exporters.Extension(Objects[id],chosen);
     dest=Path.Combine(itemFolder,name+"."+ext);Write(chosen,dest);rawFallback++;
    }
    if(!File.Exists(dest))throw new InvalidDataException("Exporter produced no file");
    success++;results.Add(new{id,ok=true,path=dest,format=chosen,requestedFormat,rawFallback=warning is not null,warning});
   }catch(Exception ex){results.Add(new{id,ok=false,error=ex.GetBaseException().Message,format});}
  }
  var report=new{created=DateTimeOffset.UtcNow,success,failed=ids.Length-success,rawFallback,results};
  File.WriteAllText(Path.Combine(output,"export-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..6]+".json"),JsonSerializer.Serialize(report,Json));
  return report;
 }
}
public sealed record ExportOptions {public bool Animations{get;init;}=true;public bool BlendShapes{get;init;}=true;public bool Materials{get;init;}=true;public float Scale{get;init;}=1;public bool AllUv{get;init;}=true;}
