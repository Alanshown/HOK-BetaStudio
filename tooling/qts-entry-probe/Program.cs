using AssetStudio;
using System.Text.Json;
using System.Security.Cryptography;
using ZstdSharp;

// Inspect one explicitly selected payload without instantiating the other
// hundreds of thousands of SerializedFiles in a DB. Never modifies its source.
string source=Path.GetFullPath(args[0]),output=Path.GetFullPath(args[2]);
ulong id=ulong.Parse(args[1]);
Directory.CreateDirectory(output);Logger.Silent=true;
using var reader=new FileReader(source);var index=new QtsVFSFile(reader);
using var payload=new MemoryStream();var chunks=new List<object>();
foreach(var c in index.Entries[id].OrderBy(c=>c.MainBlock).ThenBy(c=>c.SubBlock).ThenBy(c=>c.Offset)){
 reader.Position=c.Offset;var raw=reader.ReadBytes(c.CompressedSize);if(raw.Length!=c.CompressedSize)throw new EndOfStreamException();
 byte[] decoded;string codec;
 if(c.UncompressedSize<=1||raw.AsSpan().StartsWith("QTSF_PACKAGE"u8)){decoded=raw;codec="stored";}
 else{decoded=new byte[c.UncompressedSize];int size;
  if(raw.AsSpan().StartsWith(new byte[]{0x28,0xb5,0x2f,0xfd})){using var z=new Decompressor();size=z.Unwrap(raw,0,raw.Length,decoded,0,decoded.Length);codec="zstd";}
  else if(raw.Length>=2&&(raw[0]==0x8c||raw[0]==0xcc)&&raw[1]==0x0c){size=OozHelper.Decompress(raw,decoded);codec="oodle";}
  else{size=LZ4.Instance.Decompress(raw,decoded);codec="lz4";}
  if(size!=decoded.Length)throw new InvalidDataException($"Decoded {size}/{decoded.Length}");
 }
 payload.Write(decoded);chunks.Add(new{c.Offset,c.CompressedSize,c.UncompressedSize,c.MainBlock,c.SubBlock,codec});
}
var bytes=payload.ToArray();string target=Path.Combine(output,id+".payload");File.WriteAllBytes(target,bytes);
using var test=new FileReader(target);string error=null;object metadata=null;
if(test.FileType==FileType.AssetsFile){try{var file=new SerializedFile(test,new AssetsManager{Game=GameManager.GetGame(GameType.HonorOfKings)});metadata=new{file.unityVersion,objects=file.m_Objects.Count,classes=file.m_Objects.Select(o=>o.classID),position=test.Position,typeDependencies=file.m_Types.Select(t=>new{t.classID,dependencies=t.m_TypeDependencies}),managedTypes=file.m_RefTypes?.Select(t=>new{index=t.m_ScriptTypeIndex,name=t.m_KlassName,nameSpace=t.m_NameSpace,assembly=t.m_AsmName}),externalFiles=file.m_Externals.Select(e=>new{e.guid,e.type,e.pathName})};}catch(Exception e){error=e.ToString();}}
var report=new{source,id=id.ToString(),bytes=bytes.Length,sha256=Convert.ToHexString(SHA256.HashData(bytes)),fileType=test.FileType.ToString(),chunks,metadata,error,parserPosition=test.Position,first256=Convert.ToHexString(bytes.AsSpan(0,Math.Min(256,bytes.Length)))};
File.WriteAllText(Path.Combine(output,"report.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(report));
