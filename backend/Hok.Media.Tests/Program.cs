using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Hok.Desktop;
namespace Hok.Worker;
internal static class Program {
 internal static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
 static void Assert(bool value,string message){if(!value)throw new Exception(message);}
 static void Reject(Action action){try{action();}catch(InvalidDataException){return;}catch(IOException){return;}throw new Exception("Expected a safe rejection");}
 static byte[] B(string tag,byte[] data){var b=new byte[8+data.Length];Encoding.ASCII.GetBytes(tag).CopyTo(b,0);Put(b,4,(uint)data.Length);data.CopyTo(b,8);return b;}
 static void Put(byte[] b,int p,uint value)=>BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(p),value);
 static ResourceAsset Bank(byte[] bytes)=>new("bank","test.bnk","fixture","WwiseBank","bnk",bytes);
 static void Main(string[] args){
  var results=new List<string>();
  var wave=Encoding.ASCII.GetBytes("RIFF1234WAVEdata");var idx=new byte[12];Put(idx,0,123);Put(idx,8,(uint)wave.Length);
  var bank=B("BKHD",new byte[4]).Concat(B("DIDX",idx)).Concat(B("DATA",wave)).ToArray();
  var media=WwiseIndex.Expand(Bank(bank)).ToArray();Assert(media.Length==1&&media[0].Name=="123.wem"&&media[0].Data.SequenceEqual(wave),"BNK indexed bytes");results.Add("SoundBank DIDX ranges preserve exact media bytes");
  Reject(()=>WwiseIndex.Expand(Bank(bank[..^1])).ToArray());results.Add("truncated bank range rejected");
  var invalid=(byte[])idx.Clone();Put(invalid,4,uint.MaxValue);Reject(()=>WwiseIndex.Expand(Bank(B("DIDX",invalid).Concat(B("DATA",wave)).ToArray())).ToArray());results.Add("overflowing media offset rejected");
  Reject(()=>WwiseIndex.Expand(Bank(B("DIDX",new byte[11]).Concat(B("DATA",wave)).ToArray())).ToArray());results.Add("non-record-aligned media index rejected");
  var pack=new byte[64+bank.Length];Encoding.ASCII.GetBytes("AKPK").CopyTo(pack,0);Put(pack,4,56);Put(pack,8,1);Put(pack,12,4);Put(pack,16,24);Put(pack,20,4);Put(pack,24,4);Put(pack,32,1);Put(pack,36,555);Put(pack,40,1);Put(pack,44,(uint)bank.Length);Put(pack,48,64);bank.CopyTo(pack,64);
  ResourceAsset Package(byte[] data)=>new("pack","test.pck","fixture","WwisePackage","pck",data);
  Assert(WwiseIndex.Expand(Package(pack)).Count()==2,"Package contains bank and media");results.Add("PCK table expands bank and nested media");
  var bad=(byte[])pack.Clone();Put(bad,8,0);Reject(()=>WwiseIndex.Expand(Package(bad)).ToArray());results.Add("unsupported PCK byte order rejected");
  bad=(byte[])pack.Clone();Put(bad,4,20);Reject(()=>WwiseIndex.Expand(Package(bad)).ToArray());results.Add("PCK table cannot escape declared header");
  bad=(byte[])pack.Clone();Put(bad,48,0);Reject(()=>WwiseIndex.Expand(Package(bad)).ToArray());results.Add("PCK media cannot overlap header");
  var root=Directory.CreateTempSubdirectory("hok-cache-safety-").FullName;var parent=Path.Combine(root,"cache");var session=Path.Combine(parent,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(session,"nested"));File.WriteAllBytes(Path.Combine(session,"nested","preview.wav"),[1,2,3]);File.WriteAllText(Path.Combine(root,"source.db"),"preserve");
  Assert(OwnedCache.Clear(session,parent)==3&&Directory.Exists(session)&&!Directory.EnumerateFileSystemEntries(session).Any(),"Session clean");Assert(File.ReadAllText(Path.Combine(root,"source.db"))=="preserve","Outside untouched");results.Add("only GUID session contents removed; root and outside source preserved");
  foreach(var candidate in new[]{root,parent,Path.Combine(parent,"not-a-session"),Path.Combine(session,Guid.NewGuid().ToString("N"))})Reject(()=>OwnedCache.Clear(candidate,parent));results.Add("workspace, cache parent, non-GUID and nested cleanup targets rejected");
  var resource=new ResourceAsset("raw","raw.bin","fixture","ResourceFile","bin",[0,1,2,3]);Assert(resource.Formats.Contains("original")&&!resource.Formats.Contains("mp3"),"Unknown conversion");var output=Path.Combine(root,"raw.bin");resource.Write("original",output);Assert(File.ReadAllBytes(output).SequenceEqual(resource.Data),"Unknown original preserved");results.Add("unknown resource export remains byte-exact without invented conversion formats");
  var zip=Path.Combine(root,"bank.zip");BankArchive.Write(Bank(bank),"zip-wem",zip);
  using(var archive=System.IO.Compression.ZipFile.OpenRead(zip)){Assert(archive.Entries.Count==1&&archive.Entries[0].FullName=="123.wem","ZIP media name");using var stream=archive.Entries[0].Open();using var memory=new MemoryStream();stream.CopyTo(memory);Assert(memory.ToArray().SequenceEqual(wave),"ZIP original media");}
  results.Add("ZIP contains exact WEM bytes with safe media-ID filename");
  var invalidZip=Path.Combine(root,"invalid.zip");Reject(()=>BankArchive.Write(Bank(bank[..^1]),"zip-wem",invalidZip));Assert(!File.Exists(invalidZip),"No invalid ZIP");results.Add("malformed bank cannot produce a successful or incomplete final ZIP");
  var emptyZip=Path.Combine(root,"empty.zip");Reject(()=>BankArchive.Write(Bank(B("BKHD",new byte[4])),"zip-wem",emptyZip));Assert(!File.Exists(emptyZip),"No empty archive");results.Add("external-media-only bank reports no embedded media instead of an empty successful ZIP");
  var failedZip=Path.Combine(root,"failed.zip");Reject(()=>BankArchive.Write(Bank(bank),"zip-mp3",failedZip));Assert(!File.Exists(failedZip)&&!Directory.EnumerateFiles(root,"*.partial").Any(),"Failed conversion cleaned");results.Add("conversion failure leaves no final ZIP or partial artifact on handled failure");
  var report=new{date=DateTimeOffset.UtcNow,passed=results.Count,results};Console.WriteLine(JsonSerializer.Serialize(report,Json));if(args.Length>0)File.WriteAllText(Path.Combine(args[0],"planning/media-safety-test-report.json"),JsonSerializer.Serialize(report,Json));
 }
}
