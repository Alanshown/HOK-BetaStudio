// Adapted from the user-supplied HOK PackedQuaternion review harness.
// Run the generated apphost executable (not dotnet DLL) so native DLL lookup is correct.
using AssetStudio;using System.Reflection;using System.Collections;using System.Text.Json;using System.Text.Json.Serialization;using System.Security.Cryptography;using AO=AssetStudio.Object;
Logger.Silent=true;Progress.Silent=true;
if(args.Length!=2)throw new ArgumentException("<output-directory> <one-source-db>; use CLIP_ID_FILE or CLIP_IDS for targets");
string output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);
string Hash(byte[] x)=>Convert.ToHexString(SHA256.HashData(x)).ToLowerInvariant();
var options=new JsonSerializerOptions{WriteIndented=false,NumberHandling=JsonNumberHandling.AllowNamedFloatingPointLiterals};
void Save(string n,object? x)=>File.WriteAllText(Path.Combine(output,n),JsonSerializer.Serialize(x,options));
var ids=new HashSet<long>{6451100537591284481,2660802935491393909,1989524389377996709,-4450975541292562003,5963907614473128288,8078772706737308171,1415915779324595615};
if(Environment.GetEnvironmentVariable("CLIP_IDS") is string extra)foreach(var v in extra.Split(',',StringSplitOptions.RemoveEmptyEntries))ids.Add(long.Parse(v));
var manager=new AssetsManager{Game=GameManager.GetGame(GameType.HonorOfKings),QtsCacheDirectory=Path.Combine(output,"cache"),DeferHeavyObjects=true};
var sources=args.Skip(1).Select(Path.GetFullPath).ToArray();var hashes=sources.ToDictionary(x=>x,x=>Hash(File.ReadAllBytes(x)));
if(Environment.GetEnvironmentVariable("CLIP_ID_FILE") is string idfile)foreach(var v in File.ReadAllText(idfile).Trim().Split(',',StringSplitOptions.RemoveEmptyEntries))ids.Add(long.Parse(v));
manager.LoadFilesReadOnly(sources);Console.WriteLine($"Loaded {manager.assetsFileList.Count} serialized files");
object? Project(object? x,int depth=0){
 if(x==null||depth>40)return null;var t=x.GetType();if(x is string||t.IsPrimitive||x is decimal)return x;if(t.IsEnum)return x.ToString();
 if(t.IsGenericType&&t.GetGenericTypeDefinition()==typeof(KeyValuePair<,>))return new{key=Project(t.GetProperty("Key")!.GetValue(x),depth+1),value=Project(t.GetProperty("Value")!.GetValue(x),depth+1)};
 if(t.IsGenericType&&t.GetGenericTypeDefinition()==typeof(PPtr<>))return new{m_FileID=t.GetField("m_FileID")!.GetValue(x),m_PathID=t.GetField("m_PathID")!.GetValue(x)?.ToString()};
 if(x is IDictionary dic){var d=new Dictionary<string,object?>();foreach(DictionaryEntry p in dic)d[p.Key.ToString()!]=Project(p.Value,depth+1);return d;}
 if(x is IEnumerable list)return list.Cast<object?>().Select(v=>Project(v,depth+1)).ToArray();
 var r=new Dictionary<string,object?>();foreach(var f in t.GetFields(BindingFlags.Public|BindingFlags.Instance)){
  if(f.DeclaringType==typeof(AO)||f.Name is "version" or "buildType" or "platform")continue;
  if(typeof(SerializedFile).IsAssignableFrom(f.FieldType)||typeof(ObjectReader).IsAssignableFrom(f.FieldType))continue;
  r[f.Name]=Project(f.GetValue(x),depth+1);
 }return r;
}
var reports=new List<object>();var sfrows=new List<object>();
foreach(var f in manager.assetsFileList){
 var matches=f.m_Objects.Where(i=>ids.Contains(i.m_PathID)).ToArray();if(matches.Length==0)continue;
 sfrows.Add(new{source=f.originalPath,entry=f.containerEntryId,offset=f.containerByteOffset,serialized_name=f.fileName,unity=f.unityVersion,format=(int)f.header.m_Version,userInformation=f.userInformation,types=f.m_Types.Select(t=>new{classId=t.classID,hash=Convert.ToHexString(t.m_OldTypeHash??[])}),externals=f.m_Externals.Select((v,i)=>new{fileId=i+1,guid=v.guid.ToString(),guidRaw=Convert.ToHexString(v.guid.ToByteArray()),path=v.pathName,name=v.fileName})});
 foreach(var info in matches){
  f.reader.Position=info.byteStart;byte[] raw=f.reader.ReadBytes((int)info.byteSize);string stem=$"{f.containerEntryId}_{info.m_PathID}";File.WriteAllBytes(Path.Combine(output,stem+".bin"),raw);
  string? err=null;AO? obj=null;long? consumed=null;
  try{obj=DeferredObject.Resolve(f.ObjectsDic[info.m_PathID]);consumed=obj.reader.Position-info.byteStart;Save(stem+".typed.json",Project(obj));}catch(Exception e){err=e.ToString();}
  object? flags=null;
  if(obj is AnimationClip c&&c.m_Events.Count==0&&(c.version[0]>2018||(c.version[0]==2018&&c.version[1]>=3))){int off=raw.Length-(c.m_HokUnknownTrailing?.Length??0)-8;if(off>=0)flags=new{offset=off,hasGenericRootTransform=raw[off],hasMotionFloatCurves=raw[off+1],next8=Convert.ToHexString(raw.AsSpan(off,8)),method="For zero events, flags+aligned padding immediately precede 4-byte event count and known trailing bytes"};}
  if(obj is AnimationClip exportClip&&Environment.GetEnvironmentVariable("EXPORT_CLIPS")=="1"){
   File.WriteAllText(Path.Combine(output,stem+".anim"),exportClip.Convert());
   var tracks=new List<object>();
   foreach(var v in exportClip.m_PositionCurves)tracks.Add(new{path=v.path,property="position",classId=4,keys=v.curve.m_Curve.Select(k=>new{time=k.time,value=new[]{k.value.X,k.value.Y,k.value.Z}}).ToArray()});
   foreach(var v in exportClip.m_RotationCurves)tracks.Add(new{path=v.path,property="rotation",classId=4,keys=v.curve.m_Curve.Select(k=>new{time=k.time,value=new[]{k.value.X,k.value.Y,k.value.Z,k.value.W}}).ToArray()});
   foreach(var v in exportClip.m_ScaleCurves)tracks.Add(new{path=v.path,property="scale",classId=4,keys=v.curve.m_Curve.Select(k=>new{time=k.time,value=new[]{k.value.X,k.value.Y,k.value.Z}}).ToArray()});
   foreach(var v in exportClip.m_EulerCurves)tracks.Add(new{path=v.path,property="euler",classId=4,keys=v.curve.m_Curve.Select(k=>new{time=k.time,value=new[]{k.value.X,k.value.Y,k.value.Z}}).ToArray()});
   foreach(var v in exportClip.m_FloatCurves)tracks.Add(new{path=v.path,property=v.attribute,classId=(int)v.classID,keys=v.curve.m_Curve.Select(k=>new{time=k.time,value=new[]{(float)k.value}}).ToArray()});
   Save(stem+".curves.json",new{name=exportClip.Name,sampleRate=exportClip.m_SampleRate,tracks,objectReferenceCurves=exportClip.m_PPtrCurves.Count});
  }
  reports.Add(new{source=f.originalPath,entry=f.containerEntryId,pathId=info.m_PathID.ToString(),type=info.classID,name=obj?.Name,raw_sha256=Hash(raw),bytes=raw.Length,raw_file=stem+".bin",typed_file=stem+".typed.json",consumed,parse_status=f.ParseStatuses.GetValueOrDefault(info.m_PathID),error=err,flags});Console.WriteLine($"Parsed {obj?.Name??info.m_PathID.ToString()} bytes={raw.Length} consumed={consumed} error={err!=null}");
 }
}
if(reports.Count==0)throw new InvalidDataException("No requested objects were parsed; inspect native dependencies and input identity.");
Save("SELECTED_RAW_OBJECTS.json",reports);Save("SELECTED_SERIALIZED_FILES.json",sfrows);Save("SOURCE_INTEGRITY.json",new{sources=hashes,unchanged=hashes.All(kv=>kv.Value==Hash(File.ReadAllBytes(kv.Key))),serialized_file_count=manager.assetsFileList.Count,selected_count=reports.Count,entry_count=manager.QtsDatabases.Sum(x=>x.Entries.Count())});manager.Clear();
