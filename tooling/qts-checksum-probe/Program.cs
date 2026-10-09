using System.Buffers.Binary;
using System.IO.Hashing;
using System.Text.Json;
using AssetStudio;

var matches = new Dictionary<string,int>(); int tested = 0;
foreach (var path in Directory.GetFiles(args[0],"*.db")) {
 var bytes=File.ReadAllBytes(path); using var reader=new FileReader(path); var qts=new QtsVFSFile(reader);
 foreach(var chunk in qts.Entries.Values.SelectMany(x=>x)) {
  int offset=(int)chunk.Offset-32, size=BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset+8));
  ulong stored=BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(offset+12));
  tested++;
  foreach(var start in new[]{20,24,28,32}) foreach(var end in new[]{32+chunk.CompressedSize,size}) {
   if(end<start||offset+end>bytes.Length)continue;var span=bytes.AsSpan(offset+start,end-start);
   foreach(var item in new[]{("xx64",XxHash64.HashToUInt64(span)),("xx3",XxHash3.HashToUInt64(span)),("crc64",Crc64.HashToUInt64(span))})
    if(item.Item2==stored){string key=item.Item1+":"+start+":"+(end==size?"record-end":"payload-end");matches[key]=matches.GetValueOrDefault(key)+1;}
  }
 }
}
Console.WriteLine(JsonSerializer.Serialize(new{tested,matches}));
