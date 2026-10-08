using System.Security.Cryptography;
using System.Text.Json;
namespace Hok.Desktop;
internal static class App {public static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);}
internal static class Program {
 static string Hash(string source){using var file=File.OpenRead(source);return Convert.ToHexString(SHA256.HashData(file));}
 static async Task Main(string[] args){
  string root=Path.GetFullPath(args[0]),build=Path.GetFullPath(args[1]),source=Path.GetFullPath(args[2]),output=Path.GetFullPath(args[3]);
  var corpus=File.ReadAllLines(Path.Combine(root,"tests/hok_db_corpus.txt"));if(!corpus.Contains(Path.GetDirectoryName(source),StringComparer.OrdinalIgnoreCase)||Path.GetExtension(source)!=".db")throw new InvalidOperationException("One DB from the fixed corpus only");
  Directory.CreateDirectory(output);string cache=Path.Combine(output,"preview");Directory.CreateDirectory(cache);string original=Hash(source);var results=new List<object>();
  using var worker=new WorkerClient(build,cache,Path.Combine(output,"worker.log"));
  var load=await worker.Call("load",new{paths=new[]{source}});Console.WriteLine("Loaded via desktop's real time/memory guard: "+load.GetProperty("count"));
  foreach(var type in new[]{"Mesh","AnimationClip","Material"}){
   var list=await worker.Call("list",new{type,page=0});if(list.GetProperty("total").GetInt32()==0)continue;
   var first=list.GetProperty("items")[0];string id=first.GetProperty("id").GetString()!;
   var preview=await worker.Call("preview",new{assetId=id});var file=Path.Combine(cache,preview.GetProperty("file").GetString()!);if(!File.Exists(file)||new FileInfo(file).Length==0)throw new Exception("Preview file missing");
   if(preview.GetProperty("kind").GetString()=="curves"){using var curves=JsonDocument.Parse(File.ReadAllText(file));if(curves.RootElement.GetProperty("tracks").GetArrayLength()==0)throw new Exception("No animation tracks");}
   if(type=="Material"){using var material=JsonDocument.Parse(File.ReadAllText(file));if(!material.RootElement.TryGetProperty("m_SavedProperties",out _))throw new Exception("Material parameters missing");}
   var exported=await worker.Call("export",new{assetIds=new[]{id},format=type=="Mesh"?"obj":type=="AnimationClip"?"anim":"json",output=Path.Combine(output,"exports")});if(exported.GetProperty("failed").GetInt32()!=0)throw new Exception(exported.ToString());
   var refreshed=await worker.Call("list",new{type,page=0});if(refreshed.GetProperty("items")[0].GetProperty("parseStatus").GetString() is "deferred" or "parser-failed")throw new Exception("Decoded parse status was not refreshed");
   results.Add(new{type,id,preview,exported});Console.WriteLine(type+" preview + semantic export passed");
  }
  var audio=await worker.Call("list",new{type="WwiseAudio",page=0});
  if(audio.GetProperty("total").GetInt32()>0){
   using var cancel=new CancellationTokenSource();cancel.CancelAfter(40);bool cancelled=false;
   try{await worker.Call("export",new{assetIds=audio.GetProperty("items").EnumerateArray().Select(a=>a.GetProperty("id").GetString()).ToArray(),format="mp3",output=Path.Combine(output,"cancelled-export")},cancel.Token);}
   catch(Exception e) when(e is IOException or OperationCanceledException){cancelled=e.Message.Contains("cancel",StringComparison.OrdinalIgnoreCase);}
   if(!cancelled)throw new Exception("Active export was not cancelled cooperatively");
   var after=await worker.Call("list",new{page=0});if(after.GetProperty("total").GetInt32()!=load.GetProperty("count").GetInt32())throw new Exception("Cooperative cancellation lost the workspace index");
   results.Add(new{cooperativeCancellation=true,indexPreserved=true});Console.WriteLine("Cancellation stops export and preserves the shared workspace worker");
  }
  await worker.Call("releasePreview",new{});
  if(original!=Hash(source))throw new Exception("Source DB changed");File.WriteAllText(Path.Combine(output,"report.json"),JsonSerializer.Serialize(new{source,originalUnchanged=true,load,results},new JsonSerializerOptions(App.Json){WriteIndented=true}));
 }
}
