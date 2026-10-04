using AssetStudio;
using System.Security.Cryptography;
using System.Text.Json;

// Synthetic codec checks plus optional real DB directories. No source mutation.
Logger.Silent=true;
var checks=new List<string>();
void Check(bool ok,string message){if(!ok)throw new Exception(message);checks.Add(message);Console.WriteLine("PASS "+message);}
void Reject(Action action,string message){try{action();}catch(InvalidDataException){Check(true,message);return;}throw new Exception(message);}
ushort[] Pack(params (int Width,uint Value)[][] frames){
 var words=new List<ushort>();
 foreach(var frame in frames){int bit=0;var row=new List<ushort>();
  foreach(var (width,value) in frame)for(int i=0;i<width;i++,bit++){if(bit/16==row.Count)row.Add(0);row[bit/16]|=(ushort)(((value>>i)&1)<<(bit%16));}
  words.AddRange(row);
 }return words.ToArray();
}
HokDenseRange Range(uint kind,float minimum=0,float step=1)=>new(){kind=kind,minimum=minimum,step=step};
var scalar=DenseClip.DecodeHokSamples(2,1,Pack([(8,3)],[(8,201)]),[Range(0x803,-2,.5f)]);
Check(scalar.SequenceEqual(new[]{-.5f,98.5f}),"bit-packed scalar frames align to UInt16 boundaries");
var vector=DenseClip.DecodeHokSamples(1,3,Pack([(9,1),(9,257),(9,511)]),[Range(0x902,0,.25f)]);
Check(vector.SequenceEqual(new[]{.25f,64.25f,127.75f}),"vector components span UInt16 word boundaries");
for(uint omitted=0;omitted<4;omitted++)foreach(uint sign in new uint[]{0,4}){
 var quat=DenseClip.DecodeHokSamples(1,4,Pack([(12,0),(12,0),(12,0),(3,omitted|sign)]),[Range(0xc01,0,1f/4095)]);
 Check(quat.Select((v,i)=>v==(i==omitted?(sign==0?1:-1):0)).All(v=>v),"quaternion trailing selector/sign "+omitted+"/"+sign);
}
var nonzero=DenseClip.DecodeHokSamples(1,4,Pack([(8,51),(8,102),(8,51),(3,7)]),[Range(0x801,0,1f/255)]);
Check(Math.Abs(nonzero[0]-.2f)<1e-6&&Math.Abs(nonzero[1]-.4f)<1e-6&&Math.Abs(nonzero[2]-.2f)<1e-6&&nonzero[3]<0&&Math.Abs(nonzero.Sum(x=>x*x)-1)<1e-6,"nonzero quaternion reconstructs omitted component with correct sign");
var legacy=DenseClip.DecodeHokSamples(1,4,[1,2,3,4],[Range(1,-1,.5f)]);
Check(legacy.SequenceEqual(new[]{-.5f,0,.5f,1}),"legacy full UInt16 encoding remains unchanged");
Reject(()=>DenseClip.DecodeHokSamples(2,1,[1],[Range(0x803)]),"truncated packed frame is rejected");
Reject(()=>DenseClip.DecodeHokSamples(1,1,[1],[Range(0x1103)]),"unsupported precision is rejected");
Reject(()=>DenseClip.DecodeHokSamples(1,1,[1],[Range(0x804)]),"unknown group type is rejected");
Reject(()=>DenseClip.DecodeHokSamples(1,2,[1],[Range(0x803)]),"curve dimensions must match");
Reject(()=>DenseClip.DecodeHokSamples(1,4,[0xffff,0xffff],[Range(0x801,2,1)]),"invalid quaternion is not fabricated");
using(var raw=new BinaryReader(new MemoryStream([1,2,3]))){
 Reject(()=>new ResourceReader(raw,2,2).GetData(),"short resource range is not reported as successful");
 Check(new ResourceReader(raw,0,3).GetData().SequenceEqual(new byte[]{1,2,3}),"complete resource range is byte-exact");
}
var reports=new List<object>();
foreach(string argument in args.Where(a=>!a.StartsWith("--"))){
 string source=Path.GetFullPath(argument);
 var paths=Directory.GetFiles(source,"*.db");
 string Hash(string p){using var stream=File.OpenRead(p);return Convert.ToHexString(SHA256.HashData(stream));}
 var hashes=paths.ToDictionary(p=>p,Hash);
 var manager=new AssetsManager {Game=GameManager.GetGame(GameType.HonorOfKings)};
 try{
  manager.LoadFilesReadOnly(paths);
  var statuses=manager.assetsFileList.SelectMany(f=>f.m_Objects.Select(m=>new{source=f.fullName,m.classID,m.m_PathID,status=f.ParseStatuses[m.m_PathID]})).ToArray();
  var failures=statuses.Where(r=>r.status.Status=="parser-failed").ToArray();
  int rawObjects=0;
  foreach(var f in manager.assetsFileList)foreach(var metadata in f.m_Objects){
   f.reader.Position=metadata.byteStart;var bytes=f.reader.ReadBytes(checked((int)metadata.byteSize));
   if(bytes.Length!=metadata.byteSize)throw new Exception("Incomplete original object bytes");
   if(f.ObjectsDic.TryGetValue(metadata.m_PathID,out var obj)&&!obj.GetRawData().AsSpan().SequenceEqual(bytes))throw new Exception("Raw object export changed bytes");
   rawObjects++;
  }
  Check(rawObjects==statuses.Length,Path.GetFileName(source)+": every object-table range remains raw-exportable");
  var clips=manager.assetsFileList.SelectMany(f=>f.Objects).OfType<AnimationClip>().ToArray();
  var animationFailures=new List<string>();int packed=0;
  foreach(var clip in clips){
   try{
    string yaml=clip.Convert();if(string.IsNullOrWhiteSpace(yaml)||yaml!=clip.Convert())throw new Exception("Empty/non-idempotent animation");
    var dense=clip.m_HokLegacyAnimation?.clip.m_DenseClip;
    if(dense?.m_HokRanges?.Any(r=>r.BitWidth>0)==true){
     packed++;if(!dense.m_SampleArray.All(float.IsFinite))throw new Exception("Non-finite animation values");
     if(clip.m_RotationCurves.Count+clip.m_PositionCurves.Count+clip.m_ScaleCurves.Count+clip.m_EulerCurves.Count+clip.m_FloatCurves.Count!=clip.m_HokLegacyAnimation!.bindings.Count)
      throw new Exception("Animation binding was not exported");
    }
   }catch(Exception e){animationFailures.Add(clip.assetsFile.fullName+" / "+clip.Name+": "+e.Message);}
  }
  if(args.Contains("--resource-diagnostics")){
   foreach(var texture in manager.assetsFileList.SelectMany(f=>f.Objects).OfType<Texture2D>()){
    try{_=texture.image_data.GetData();}
    catch(Exception e){
     string reference=texture.m_StreamData?.path??"";
     string hash=QtsVFSFile.Compute(reference,true).ToString();
     var matches=manager.ResourceFiles.Where(p=>p.Key.EndsWith("|"+hash,StringComparison.OrdinalIgnoreCase)||p.Key.EndsWith("|"+reference,StringComparison.OrdinalIgnoreCase)).Select(p=>p.Key).ToArray();
     Console.WriteLine(JsonSerializer.Serialize(new{missingTexture=texture.Name,source=texture.assetsFile.originalPath,reference,hash,matches,error=e.Message}));
    }
   }
  }
  var report=new{source,serializedFiles=manager.assetsFileList.Count,objects=statuses.Length,rawObjects,parserFailures=failures,animations=clips.Length,packedAnimations=packed,animationFailures,types=manager.assetsFileList.SelectMany(f=>f.m_Objects).GroupBy(m=>m.classID).ToDictionary(g=>g.Key,g=>g.Count())};
  reports.Add(report);Console.WriteLine(JsonSerializer.Serialize(report));
  Check(paths.All(p=>hashes[p]==Hash(p)),Path.GetFileName(source)+": original DB hashes unchanged");
  if(args.Contains("--strict")){Check(failures.Length==0,Path.GetFileName(source)+": no typed parser failures");Check(animationFailures.Count==0,Path.GetFileName(source)+": all animations export successfully");}
 }finally{manager.Clear();}
}
Console.WriteLine(JsonSerializer.Serialize(new{passed=checks.Count,reports}));
