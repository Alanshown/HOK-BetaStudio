using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using AssetStudio;

namespace Hok.Worker;

// Never identify a script layout by name/PathID alone. This fingerprint was
// checked against independent 14005 and 14006 SerializedFiles. Original field
// names/layout are now verified by the matching TTre.db type hash. Internal
// numeric meanings inside pipeline-state text are not inferred from that.
internal static class HokScriptSchemas
{
 internal sealed record TextField(int Offset,int ByteCount,string Value,string Sha256);
 internal sealed record Section(int Ordinal,int Offset,int End,string Interpretation,List<TextField> Strings);
 internal sealed record ShaderVariant(string Shader,string Keywords,int PassType);
 public static object? Inspect(MonoBehaviour mono)
 {
  if(mono.assetsFile.unityVersion!="2022.3.5f1"||mono.serializedType?.m_OldTypeHash is not{} type||mono.serializedType.m_ScriptID is not{} script||
    Convert.ToHexString(type)!="14B8A76D7AC245084ABCE149618CFB17"||Convert.ToHexString(script)!="86119DA98F0AA04BA5F4DAEDA89BD61E")return null;
  var raw=mono.GetRawData();
  try{return Parse(raw,32);}
  catch(Exception e)when(e is InvalidDataException or DecoderFallbackException or OverflowException)
  {return new{schema="hok-character-art-text-tables-v1",status="schema-mismatch",structureComplete=false,semanticComplete=false,error=e.Message,originalBytesRetained=true};}
 }
 internal static object Parse(byte[] raw,int start)
 {
  int p=start;var sections=new List<Section>();
  int Word(){if(p>raw.Length-4)throw new InvalidDataException("Truncated script word at "+p);int v=BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(p));p+=4;return v;}
  TextField Text()
  {
    int offset=p,n=Word();if(n<0||n>raw.Length-p)throw new InvalidDataException("Script string bounds at "+offset);
    string value=new UTF8Encoding(false,true).GetString(raw,p,n);string hash=Convert.ToHexString(SHA256.HashData(raw.AsSpan(p,n)));p+=n;
    while((p&3)!=0){if(p>=raw.Length||raw[p++]!=0)throw new InvalidDataException("Nonzero/truncated string alignment");}
    return new(offset,p-offset,value,hash);
  }
  int m_skinType=Word();sections.Add(new(0,start,p,"m_skinType: int",[]));
  int prefabAt=p;var prefab=Text();sections.Add(new(1,prefabAt,p,"m_PrefabPath: string",[prefab]));
  for(int table=2;table<=4;table++)
  {
   int at=p,count=Word();if(count<0||count>(raw.Length-p)/4)throw new InvalidDataException("Script vector count at "+at);
   var texts=new List<TextField>();for(int i=0;i<count;i++)texts.Add(Text());
   sections.Add(new(table,at,p,table switch{2=>"m_PathsData: vector<string>",3=>"m_VariantsData: vector<string>",_=>"m_PSOsData: vector<string>"},texts));
  }
  int overrideAt=p;if(p>=raw.Length)throw new InvalidDataException("Missing m_IsRFCfgOverride");byte m_IsRFCfgOverride=raw[p++];
  while((p&3)!=0){if(p>=raw.Length||raw[p++]!=0)throw new InvalidDataException("Invalid script final alignment");}
  sections.Add(new(5,overrideAt,p,"m_IsRFCfgOverride: UInt8",[]));
  if(p!=raw.Length)throw new InvalidDataException("Trailing script bytes at "+p);
  var variants=new List<ShaderVariant>();
  foreach(var text in sections[3].Strings)
  foreach(var line in text.Value.Split('\n',StringSplitOptions.RemoveEmptyEntries))
  {
   int keyword=line.IndexOf(",KN:",StringComparison.Ordinal),pass=line.LastIndexOf(",PT:",StringComparison.Ordinal);
   if(!line.StartsWith("SN:",StringComparison.Ordinal)||keyword<3||pass<keyword+4||!int.TryParse(line[(pass+4)..],out int passType))throw new InvalidDataException("Shader variant text schema mismatch");
   variants.Add(new(line[3..keyword],line[(keyword+4)..pass],passType));
  }
  return new{schema="hok-character-art-text-tables-v1",status="structure-validated-semantics-partial",structureComplete=true,semanticComplete=false,
   originalFieldNamesKnown=true,typeTreeHash="14B8A76D7AC245084ABCE149618CFB17",objectOffset=start,bytes=p-start,sections,variants,
   fields=new{m_skinType,m_PrefabPath=prefab.Value,m_PathsData=sections[2].Strings.Select(s=>s.Value),m_VariantsData=sections[3].Strings.Select(s=>s.Value),m_PSOsData=sections[4].Strings.Select(s=>s.Value),m_IsRFCfgOverride},
   references=Array.Empty<object>(),note="Field names and types come from the package TTre.db. Strings are not implicit GUID/PPtr links; pipeline-state numeric meanings remain unconfirmed."};
 }
}
