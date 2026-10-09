using AssetStudio;
using System.Security.Cryptography;
using System.Text.Json;
using ZstdSharp;
internal static class Recovery
{
 public static void Run(string corpus,string output){
  Directory.CreateDirectory(output);var summaries=new List<object>();
  foreach(var dir in File.ReadAllLines(corpus).Where(x=>!string.IsNullOrWhiteSpace(x)))foreach(var path in Directory.GetFiles(dir,"*.db").OrderBy(x=>x)){
   using var reader=new FileReader(path);reader.Endian=EndianType.LittleEndian;var q=new QtsVFSFile(reader);
   string key=Path.GetFileName(dir)+"__"+Path.GetFileNameWithoutExtension(path);using var rows=new StreamWriter(Path.Combine(output,key+".entries.jsonl"));
   var signatures=new Dictionary<string,(int Count,string First64,string Id)>();
   long declared=0,compressed=0,recovered=0,failedBytes=0,metadataBytes=0;int chunks=0,okChunks=0,okEntries=0,failedEntries=0;
   foreach(var entry in q.Entries){
    var parts=entry.Value.OrderBy(c=>c.MainBlock).ThenBy(c=>c.SubBlock).ToArray();var diagnostics=new List<object>();using var payload=new MemoryStream();bool ok=true;long expected=0;
    foreach(var c in parts){chunks++;compressed+=c.CompressedSize;declared+=c.UncompressedSize;expected+=c.UncompressedSize;byte[] raw=[];
     try{reader.Position=c.Offset;raw=reader.ReadBytes(c.CompressedSize);if(raw.Length!=c.CompressedSize)throw new EndOfStreamException();byte[] decoded;
      if(c.UncompressedSize==0||raw.AsSpan().StartsWith("QTSF_PACKAGE"u8)){decoded=raw;metadataBytes+=decoded.Length;}
      else{decoded=new byte[c.UncompressedSize];int n;
       if(raw.AsSpan().StartsWith(new byte[]{0x28,0xb5,0x2f,0xfd})){using var z=new Decompressor();n=z.Unwrap(raw,0,raw.Length,decoded,0,decoded.Length);}
       else if(raw.Length>=2&&(raw[0]==0x8c||raw[0]==0xcc)&&raw[1]==0x0c)n=OozHelper.Decompress(raw,decoded);
       else n=LZ4.Instance.Decompress(raw,decoded);
       if(n!=decoded.Length)throw new InvalidDataException($"Decoded {n}; declared {decoded.Length}");
      }
      payload.Write(decoded);okChunks++;
     }catch(Exception e){ok=false;failedBytes+=c.UncompressedSize;diagnostics.Add(new{c.Offset,c.CompressedSize,c.UncompressedSize,c.MainBlock,c.SubBlock,first64=Convert.ToHexString(raw.AsSpan(0,Math.Min(64,raw.Length))),error=e.GetBaseException().Message});}
    }
    if(ok){okEntries++;recovered+=payload.Length;var b=payload.GetBuffer().AsSpan(0,(int)payload.Length);var key2=Convert.ToHexString(b[..Math.Min(4,b.Length)]);var old=signatures.GetValueOrDefault(key2);signatures[key2]=(old.Count+1,old.First64??Convert.ToHexString(b[..Math.Min(128,b.Length)]),old.Id??entry.Key.ToString());}else failedEntries++;
    rows.WriteLine(JsonSerializer.Serialize(new{fileId=entry.Key.ToString(),chunkCount=parts.Length,expectedBytes=expected,recoveredBytes=ok?payload.Length:0,partialBytes=ok?0:payload.Length,sha256=ok?Convert.ToHexString(SHA256.HashData(payload.GetBuffer().AsSpan(0,(int)payload.Length))):null,ok,errors=diagnostics}));
   }
   var report=new{path,entries=q.Entries.Count,chunks,okChunks,okEntries,failedEntries,declaredCompressedBytes=compressed,declaredUncompressedFieldSum=declared,recoveredPayloadBytes=recovered,storedMetadataBytes=metadataBytes,failedBytes,entryCoverage=(double)okEntries/q.Entries.Count,chunkCoverage=chunks==0?1:(double)okChunks/chunks,byteRatioIncludingStoredMetadata=declared==0?(double?)null:(double)recovered/declared};
   File.WriteAllText(Path.Combine(output,key+".signatures.json"),JsonSerializer.Serialize(signatures.OrderByDescending(s=>s.Value.Count).Take(100).Select(s=>new{magic=s.Key,count=s.Value.Count,head=s.Value.First64,fileId=s.Value.Id}),new JsonSerializerOptions{WriteIndented=true}));
   File.WriteAllText(Path.Combine(output,key+".json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));summaries.Add(report);Console.WriteLine(JsonSerializer.Serialize(report));
  }
  File.WriteAllText(Path.Combine(output,"summary.json"),JsonSerializer.Serialize(summaries,new JsonSerializerOptions{WriteIndented=true}));
 }
}
