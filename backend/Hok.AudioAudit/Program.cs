using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AssetStudio;

var root=Path.GetFullPath(args[0]);var reports=new List<object>();
foreach(var folder in args.Skip(1)){
 var paths=Directory.GetFiles(Path.GetFullPath(folder),"*.db").Order().ToArray();
 var hashes=paths.Select(p=>new{path=p,bytes=new FileInfo(p).Length,sha256=Hash(p)}).ToArray();
 var index=new List<object>();
 foreach(var p in paths){using var reader=new FileReader(p);var qts=new QtsVFSFile(reader);index.Add(new{file=Path.GetFileName(p),entries=qts.Entries.Count,payloadEntries=qts.Entries.Count(e=>e.Value.Any(c=>c.UncompressedSize>0)),emptyEntries=qts.Entries.Count(e=>e.Value.All(c=>c.UncompressedSize<=0))});}
 var log=new AuditLog();Logger.Default=log;Logger.Flags=LoggerEvent.Info|LoggerEvent.Warning|LoggerEvent.Error;
 var manager=new AssetsManager{Game=GameManager.GetGame(GameType.HonorOfKings)};
 manager.LoadFilesReadOnly(paths);
 var metadata=manager.assetsFileList.SelectMany(f=>f.m_Objects.Select(o=>new{source=f.fileName,pathId=o.m_PathID.ToString(),classId=o.classID,type=((ClassIDType)o.classID).ToString(),bytes=o.byteSize})).ToArray();
 var parsed=manager.assetsFileList.SelectMany(f=>f.Objects).ToArray();
 var absent=manager.assetsFileList.SelectMany(f=>f.m_Objects.Where(o=>!f.Objects.Any(p=>p.m_PathID==o.m_PathID)).Select(o=>new{source=f.fileName,pathId=o.m_PathID.ToString(),classId=o.classID})).ToArray();
 var streams=(Dictionary<string,BinaryReader>)typeof(AssetsManager).GetField("resourceFileReaders",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(manager)!;
 var resources=new List<object>();var signatures=new List<object>();
 foreach(var (name,reader) in streams){
  var stream=reader.BaseStream;var position=stream.Position;try{stream.Position=0;if(stream.Length>512L*1024*1024)throw new IOException("Audit stream bound");var bytes=reader.ReadBytes((int)stream.Length);var hits=Scan(bytes);
   resources.Add(new{name,bytes=bytes.Length,head=Convert.ToHexString(bytes.AsSpan(0,Math.Min(32,bytes.Length))),ascii=Encoding.ASCII.GetString(bytes.AsSpan(0,Math.Min(80,bytes.Length))),signatures=hits,textureReferences=parsed.OfType<Texture2D>().Where(t=>!string.IsNullOrEmpty(t.m_StreamData?.path)&&(Path.GetFileName(t.m_StreamData.path)==name||QtsVFSFile.Compute(t.m_StreamData.path,true).ToString()==name)).Select(t=>new{name=t.Name,format=t.m_TextureFormat.ToString(),offset=t.m_StreamData.offset,bytes=t.m_StreamData.size,wholeStream=t.m_StreamData.offset==0&&t.m_StreamData.size==bytes.Length}).ToArray(),wwise=InspectBank(bytes)});
   foreach(var hit in hits)signatures.Add(new{source=name,scope="resource",hit});
  }finally{stream.Position=position;}
 }
 foreach(var file in manager.assetsFileList){
  var reader=file.reader;var position=reader.Position;
  try{reader.Position=0;var bytes=reader.ReadBytes(checked((int)reader.BaseStream.Length));foreach(var hit in Scan(bytes))signatures.Add(new{source=file.fileName,scope="serialized-file",hit});}
  finally{reader.Position=position;}
 }
 var named=parsed.Where(o=>Regex.IsMatch(o.Name??"","audio|sound|voice|wwise|akbank|\\.(wem|bnk|ogg|wav|mp3|fsb)$",RegexOptions.IgnoreCase)).Select(o=>new{name=o.Name,type=o.type.ToString(),source=o.assetsFile.fileName}).ToArray();
 var types=metadata.GroupBy(o=>o.type).ToDictionary(g=>g.Key,g=>g.Count());
 var files=manager.assetsFileList.Select(f=>new{source=f.fileName,objects=f.m_Objects.Count,parsed=f.Objects.Count}).ToArray();
 reports.Add(new{folder=Path.GetFullPath(folder),inputs=hashes,qts=index,serializedFiles=files,declaredObjects=metadata.Length,parsedObjects=parsed.Length,declaredTypes=types,parsedTypes=parsed.GroupBy(o=>o.type.ToString()).ToDictionary(g=>g.Key,g=>g.Count()),declaredAudioClips=metadata.Count(o=>o.classId==83),parsedAudioClips=parsed.Count(o=>o is AudioClip),missingObjects=absent,resourceStreams=resources,audioSignatureCandidates=signatures,audioNamedCandidates=named,errors=log.Errors});
 manager.Clear();
 foreach(var item in hashes)if(Hash(item.path)!=item.sha256)throw new IOException("Input changed during read-only audit");
 Console.WriteLine(JsonSerializer.Serialize(new{folder,objects=metadata.Length,parsed=parsed.Length,audioClips=metadata.Count(o=>o.classId==83),resourceStreams=resources.Count,signatureCandidates=signatures.Count,errors=log.Errors.Count}));
}
var output=Path.Combine(root,"planning/audio-audit-report.json");
File.WriteAllText(output,JsonSerializer.Serialize(new{date=DateTimeOffset.UtcNow,scope="Read-only audit of supplied main DB + shard pairs; object table compared with parsed objects, decompressed resource and serialized bytes scanned for common audio container signatures.",signatureLimit="Signature absence cannot exclude proprietary, headerless or encrypted audio. This report does not assert the location of audio in other game packages.",inputHashesUnchanged=true,reports},new JsonSerializerOptions{WriteIndented=true}));
static string Hash(string path){using var stream=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(stream));}
static object[] Scan(byte[] bytes){
 var results=new List<object>();
 foreach(var signature in new[]{"RIFF","RIFX","OggS","fLaC","FSB4","FSB5","BKHD","AKPK","ID3"}){
  var marker=Encoding.ASCII.GetBytes(signature);int offset=0;
  while(offset<bytes.Length){int at=bytes.AsSpan(offset).IndexOf(marker);if(at<0)break;at+=offset;
   bool plausible=signature switch{
    "RIFF" or "RIFX"=>at+12<=bytes.Length&&(Encoding.ASCII.GetString(bytes,at+8,4) is "WAVE" or "XWMA"),
    "ID3"=>at+10<=bytes.Length&&bytes[at+3] is >=2 and <=4&&bytes[at+4]!=255&&bytes.AsSpan(at+6,4).ToArray().All(b=>b<128),
    "OggS"=>at+27<=bytes.Length&&bytes[at+4]==0,
    "BKHD"=>at+12<=bytes.Length&&BitConverter.ToUInt32(bytes,at+4)<=bytes.Length-at-8,
    _=>true};
   if(plausible)results.Add(new{signature,offset=at});offset=at+marker.Length;
  }
 }
 return results.ToArray();
}
static object? InspectBank(byte[] bytes){
 if(!bytes.AsSpan().StartsWith("AKPK"u8))return null;
 var start=bytes.AsSpan().IndexOf("BKHD"u8);if(start<0)return new{container="AKPK",bankFound=false};
 int pos=start,dataStart=-1,dataSize=0;var sections=new List<object>();var entries=new List<(uint id,uint offset,uint size)>();
 while(pos+8<=bytes.Length){
  var tag=Encoding.ASCII.GetString(bytes,pos,4);var size=BitConverter.ToUInt32(bytes,pos+4);if(size>bytes.Length-pos-8)throw new InvalidDataException("Bank chunk out of bounds");
  sections.Add(new{tag,offset=pos,bytes=size});
  if(tag=="DIDX"){if(size%12!=0)throw new InvalidDataException("DIDX length");for(int i=0;i<size;i+=12)entries.Add((BitConverter.ToUInt32(bytes,pos+8+i),BitConverter.ToUInt32(bytes,pos+12+i),BitConverter.ToUInt32(bytes,pos+16+i)));}
  if(tag=="DATA"){dataStart=pos+8;dataSize=(int)size;}
  pos+=checked(8+(int)size);
 }
 var media=new List<object>();
 foreach(var e in entries){
  if(dataStart<0||e.offset>(long)dataSize-e.size)throw new InvalidDataException("Media range out of bounds");
  var begin=checked(dataStart+(int)e.offset);var end=checked(begin+(int)e.size);
  if(e.size<12||Encoding.ASCII.GetString(bytes,begin,4)!="RIFF"||Encoding.ASCII.GetString(bytes,begin+8,4)!="WAVE")throw new InvalidDataException("Expected RIFF WAVE media");
  var riffBytes=BitConverter.ToUInt32(bytes,begin+4)+8L;if(riffBytes!=e.size)throw new InvalidDataException("WEM size does not match index");
  int p=begin+12;ushort? codec=null,channels=null;uint? sampleRate=null;int payloadBytes=0;
  while(p+8<=end){var tag=Encoding.ASCII.GetString(bytes,p,4);var size=BitConverter.ToUInt32(bytes,p+4);if(size>end-p-8)throw new InvalidDataException("WEM chunk bounds");if(tag=="fmt "&&size>=16){codec=BitConverter.ToUInt16(bytes,p+8);channels=BitConverter.ToUInt16(bytes,p+10);sampleRate=BitConverter.ToUInt32(bytes,p+12);}if(tag=="data")payloadBytes=(int)size;p+=checked(8+(int)size+((int)size&1));}
  media.Add(new{id=e.id,offset=begin,bytes=e.size,riffSizeMatchesIndex=true,formatTag=codec.HasValue?$"0x{codec:X4}":null,channels,sampleRate,payloadBytes});
 }
 return new{container="AKPK",bankOffset=start,sections,indexedMedia=media.Count,media};
}
sealed class AuditLog:ILogger{
 public List<string> Errors{get;}=[];
 public void Log(LoggerEvent level,string message){if(level is LoggerEvent.Error or LoggerEvent.Warning)Errors.Add(message);}
}
