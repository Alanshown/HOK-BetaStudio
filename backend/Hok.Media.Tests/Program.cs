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
  byte[] ActionFile(uint variableHash=0x72f31961){using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);writer.Write(1001);writer.Write(0);writer.Write(0);writer.Write(0);writer.Write(1);writer.Write(5);writer.Write("Actor"u8);writer.Write(7);writer.Write(1);writer.Write(3);writer.Write("pos"u8);writer.Write(variableHash);writer.Write(-1000);writer.Write(0);writer.Write(1000);writer.Write(2000);writer.Write((byte)0);writer.Write(1);writer.Write(0xc10a035cu);writer.Write(11);writer.Write(7);writer.Write("Play_fx"u8);writer.Write(0x1234);var bytes=stream.ToArray();Put(bytes,4,(uint)bytes.Length);return bytes;}
  ResourceAsset Timeline(byte[] data)=>new("action","action","fixture","HokActionTimeline","action",data);
  var timeline=ActionFile();var action=JsonSerializer.SerializeToElement(HokStructured.Parse(Timeline(timeline)),Json);
  Assert(action.GetProperty("structureComplete").GetBoolean()&&action.GetProperty("structureBytes").GetInt32()==timeline.Length&&action.GetProperty("events").GetArrayLength()==1,"Action tables cover the complete file");
  Assert(action.GetProperty("variables")[0].GetProperty("value")[0].GetInt32()==-1000&&action.GetProperty("events")[0].GetProperty("embeddedLengthPrefixedStrings")[0].GetProperty("value").GetString()=="Play_fx","Action values and references preserved");
  results.Add("action actors, signed vectors, bounded events and terminal marker validate without invented runtime meanings");
  var badAction=(byte[])timeline.Clone();badAction[^1]=1;Reject(()=>HokStructured.Parse(Timeline(badAction)));Reject(()=>HokStructured.Parse(Timeline(timeline[..^1])));
  results.Add("malformed action length and terminal marker rejected");
  var unknownAction=JsonSerializer.SerializeToElement(HokStructured.Parse(Timeline(ActionFile(0xabcdef01))),Json);
  Assert(!unknownAction.GetProperty("structureComplete").GetBoolean()&&unknownAction.GetProperty("parseStatus").GetString()=="typed-partial"&&!unknownAction.TryGetProperty("events",out _),"Unknown variable cannot fabricate events");results.Add("unknown action parameter hashes remain explicit partial data");
  var bmpHead=new byte[26];bmpHead[0]=(byte)'B';bmpHead[1]=(byte)'M';Put(bmpHead,2,4096);Assert(ResourceAsset.Detect(bmpHead,"",4096).extension=="bmp","Disk-backed bitmap length");results.Add("bitmap detection compares full payload length, not a cached header length");
  var fmt=new byte[16];BinaryPrimitives.WriteUInt16LittleEndian(fmt,1);BinaryPrimitives.WriteUInt16LittleEndian(fmt.AsSpan(2),1);Put(fmt,4,22050);Put(fmt,8,44100);BinaryPrimitives.WriteUInt16LittleEndian(fmt.AsSpan(12),2);BinaryPrimitives.WriteUInt16LittleEndian(fmt.AsSpan(14),16);
  var wave=B("RIFF",Encoding.ASCII.GetBytes("WAVE").Concat(B("fmt ",fmt)).Concat(B("data",new byte[]{1,0,2,0})).ToArray());var idx=new byte[12];Put(idx,0,123);Put(idx,8,(uint)wave.Length);
  var bank=B("BKHD",new byte[8]).Concat(B("DIDX",idx)).Concat(B("DATA",wave)).ToArray();
  Assert(AudioValidation.ValidateWave(wave,true).DataBytes==4,"Valid PCM");AudioValidation.ValidateBank(bank);results.Add("full RIFF/PCM and SoundBank structures validate");
  Reject(()=>AudioValidation.ValidateWave(wave[..^1],true));
  var broken=(byte[])wave.Clone();Put(broken,4,unchecked((uint)wave.Length+100));Reject(()=>AudioValidation.ValidateWave(broken,false));
  broken=(byte[])wave.Clone();Put(broken,40,1000);Reject(()=>AudioValidation.ValidateWave(broken,true));
  broken=(byte[])wave.Clone();Put(broken,40,0);Reject(()=>AudioValidation.ValidateWave(broken,true));
  broken=(byte[])wave.Clone();BinaryPrimitives.WriteUInt16LittleEndian(broken.AsSpan(32),1);Reject(()=>AudioValidation.ValidateWave(broken,true));
  results.Add("truncated RIFF, invalid chunk ranges, empty audio and PCM alignment rejected");
  var oddFmt=(byte[])fmt.Clone();BinaryPrimitives.WriteUInt16LittleEndian(oddFmt,0xffff);var odd=B("RIFF",Encoding.ASCII.GetBytes("WAVE").Concat(B("fmt ",oddFmt)).Concat(B("data",new byte[]{1,2,3})).ToArray());
  Assert(AudioValidation.ValidateWave(odd,false).DataBytes==3,"Wwise terminal odd payload is valid");Reject(()=>AudioValidation.ValidateWave(odd,true));results.Add("valid unpadded Wwise final chunk accepted, compressed WEM not mistaken for PCM");
  Reject(()=>AudioValidation.ValidateBank(bank[..^1]));Reject(()=>AudioValidation.ValidateBank(B("BKHD",new byte[4])));
  results.Add("incomplete bank header and trailing chunks rejected");
  ResourceAsset.ResetMetrics();var media=WwiseIndex.Expand(Bank(bank)).ToArray();
  Assert(ResourceAsset.MaterializedBytes==0&&media.All(m=>m.Bytes.Length==0&&m.SliceParent is not null),"BNK indexing does not materialize media payloads");
  Assert(media.Length==1&&media[0].Name=="123.wem"&&media[0].Data.SequenceEqual(wave),"BNK indexed bytes");results.Add("SoundBank DIDX indexing is lazy and selected media bytes remain exact");
  Reject(()=>WwiseIndex.Expand(Bank(bank[..^1])).ToArray());results.Add("truncated bank range rejected");
  var invalid=(byte[])idx.Clone();Put(invalid,4,uint.MaxValue);Reject(()=>WwiseIndex.Expand(Bank(B("BKHD",new byte[8]).Concat(B("DIDX",invalid)).Concat(B("DATA",wave)).ToArray())).ToArray());results.Add("overflowing media offset rejected");
  Reject(()=>WwiseIndex.Expand(Bank(B("BKHD",new byte[8]).Concat(B("DIDX",new byte[11])).Concat(B("DATA",wave)).ToArray())).ToArray());results.Add("non-record-aligned media index rejected");
  Reject(()=>WwiseIndex.Expand(Bank(bank.Concat(B("DIDX",idx)).ToArray())).ToArray());Reject(()=>WwiseIndex.Expand(Bank(bank.Concat(B("DATA",wave)).ToArray())).ToArray());results.Add("duplicate DIDX and DATA sections rejected instead of silently choosing the last");
  Reject(()=>WwiseIndex.Expand(Bank(B("BKHD",new byte[8]).Concat(B("DIDX",idx.Concat(idx).ToArray())).Concat(B("DATA",wave)).ToArray())).ToArray());results.Add("duplicate media IDs rejected before overwriting resource identity");
  var pack=new byte[64+bank.Length];Encoding.ASCII.GetBytes("AKPK").CopyTo(pack,0);Put(pack,4,56);Put(pack,8,1);Put(pack,12,4);Put(pack,16,24);Put(pack,20,4);Put(pack,24,4);Put(pack,32,1);Put(pack,36,555);Put(pack,40,1);Put(pack,44,(uint)bank.Length);Put(pack,48,64);bank.CopyTo(pack,64);
  ResourceAsset Package(byte[] data)=>new("pack","test.pck","fixture","WwisePackage","pck",data);
  ResourceAsset.ResetMetrics();var members=WwiseIndex.Expand(Package(pack)).ToArray();Assert(members.Length==2&&ResourceAsset.MaterializedBytes==0&&members.All(m=>m.Bytes.Length==0),"PCK contains bank and media without copies");
  Assert(members.Single(m=>m.IsAudio).Data.SequenceEqual(wave),"Nested member can be read after index streams close");results.Add("PCK and nested BNK expand without materializing unused audio; offset views remain valid");
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
  var emptyZip=Path.Combine(root,"empty.zip");Reject(()=>BankArchive.Write(Bank(B("BKHD",new byte[8])),"zip-wem",emptyZip));Assert(!File.Exists(emptyZip),"No empty archive");results.Add("external-media-only bank reports no embedded media instead of an empty successful ZIP");
  var failedZip=Path.Combine(root,"failed.zip");Reject(()=>BankArchive.Write(Bank(bank),"zip-mp3",failedZip));Assert(!File.Exists(failedZip)&&!Directory.EnumerateFiles(root,"*.partial").Any(),"Failed conversion cleaned");results.Add("conversion failure leaves no final ZIP or partial artifact on handled failure");
  var malformed=new ResourceAsset("bad","123.wem","fixture","WwiseAudio","wem",wave[..^1]);var failedWem=Path.Combine(root,"invalid.wem");Reject(()=>malformed.Write("wem",failedWem));Assert(!File.Exists(failedWem),"Invalid original must not be exported as valid");malformed.Write("raw",failedWem);Assert(File.ReadAllBytes(failedWem).SequenceEqual(malformed.Data),"Raw remains byte-exact");results.Add("invalid WEM semantic export rejected, explicit raw preserved");
  var report=new{date=DateTimeOffset.UtcNow,passed=results.Count,results};Console.WriteLine(JsonSerializer.Serialize(report,Json));if(args.Length>0)File.WriteAllText(Path.Combine(args[0],"planning/media-safety-test-report.json"),JsonSerializer.Serialize(report,Json));
 }
}
