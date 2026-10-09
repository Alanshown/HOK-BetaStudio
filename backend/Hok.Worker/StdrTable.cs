using System.Buffers.Binary;
using System.Text;

namespace Hok.Worker;

// Observed stdr/MSES layout, verified against three independent tables. This
// reads record boundaries and the complete UTF-8 pool; it does not invent the
// missing field schema or interpret opaque record handles as resource IDs.
internal static class StdrTable
{
 internal sealed record StringEntry(int Offset,string OpaqueKeyHex,string Text,int EndOffset);
 internal sealed record StringReference(int FieldOffset,int AbsoluteOffset,int StringPoolOffset,string Text,
  string RawHex,string Evidence="fingerprint-validated table-relative string handle; not a Unity PPtr or QTS resource ID");
 internal sealed record Record(int Index,int Offset,uint FirstWord,string RawHex,List<StringReference> StringReferences);
 internal sealed record Document(string Format,int Bytes,int RecordStride,int RecordCount,
  int StringCount,int StringPoolOffset,string HeaderHex,List<Record> Records,List<StringEntry> Strings,
  bool StructureComplete=true,bool SemanticComplete=false,
  string Note="Original record field names/types remain unknown. Three fingerprinted table layouts have validated record-to-string handles. FirstWord is retained without inventing primary-key semantics. Resource paths still need independent QTS/asset resolution; opaque pool keys are not Unity GUIDs or QTS IDs.");
 internal static bool Detect(ReadOnlySpan<byte> head,long bytes)=>head.Length>=152&&bytes>=152&&
  head[..4].SequenceEqual("stdr"u8)&&head.Slice(12,4).SequenceEqual("MSES"u8)&&
  BinaryPrimitives.ReadUInt32LittleEndian(head[16..])==8&&head.Slice(76,6).SequenceEqual("UTF-8\0"u8);
 internal static Document Parse(byte[] bytes)
 {
  if(!Detect(bytes,bytes.Length))throw new InvalidDataException("Unsupported stdr table header");
  int Count(int offset)=>checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset)));
  int strings=Count(8),stride=Count(20),count=Count(24),end=Count(144);
  if(stride<4||(stride&3)!=0||count<0||count>(bytes.Length-152)/stride||end!=152L+(long)stride*count||
    end>bytes.Length||strings<0||strings>(bytes.Length-end)/12||Count(148)!=0)
   throw new InvalidDataException("Invalid stdr record/string table bounds");
  var records=new List<Record>(count);for(int i=0,p=152;i<count;i++,p+=stride)
   records.Add(new(i,p,BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(p)),Convert.ToHexString(bytes.AsSpan(p,stride)),[]));
  var pool=new List<StringEntry>(strings);var utf8=new UTF8Encoding(false,true);int at=end;
  for(int i=0;i<strings;i++)
  {
   if(at>bytes.Length-9)throw new InvalidDataException("Truncated stdr string pool");
   int start=at;string key=Convert.ToHexString(bytes.AsSpan(at,8));at+=8;
   int zero=Array.IndexOf(bytes,(byte)0,at);if(zero<0)throw new InvalidDataException("Unterminated stdr UTF-8 string");
   string value=utf8.GetString(bytes,at,zero-at);at=zero+1;
   while((at&3)!=0){if(at>=bytes.Length||bytes[at++]!=0)throw new InvalidDataException("Invalid stdr string padding");}
   pool.Add(new(start,key,value,at));
  }
  if(at!=bytes.Length)throw new InvalidDataException("Trailing stdr bytes after declared string pool");
  string fingerprint=Encoding.ASCII.GetString(bytes,108,32);
  uint? tag=(fingerprint,stride) switch{
   ("f00ade8201e902a5886f3953759b6374",144)=>0x6c60bc42u,
   ("f89eb59792dcad2eb2d39d4b7a6f443b",1080)=>0x76275267u,
   ("d4daf91220922f24f10e5dd2c674aa4f",1144)=>0x51885180u,_=>null};
  if(tag is{} signature)
  {
   var byOffset=pool.ToDictionary(s=>s.Offset);
   foreach(var record in records)for(int f=0;f<=stride-8;f+=4)
   {
    int p=record.Offset+f;if(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(p))!=signature)continue;
    uint packed=BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(p+4));int target=(int)(packed>>6);
    if((packed&63)!=0||!byOffset.TryGetValue(target,out var text))throw new InvalidDataException("Invalid fingerprinted stdr string handle at "+p);
    record.StringReferences.Add(new(f,p,target,text.Text,Convert.ToHexString(bytes.AsSpan(p,8))));f+=4;
   }
  }
  return new("stdr-MSES-8",bytes.Length,stride,count,strings,end,Convert.ToHexString(bytes.AsSpan(0,152)),records,pool);
 }
}
