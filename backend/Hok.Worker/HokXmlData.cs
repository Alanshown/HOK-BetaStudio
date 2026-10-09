using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Hok.Worker;

internal static class HokXmlData
{
 internal sealed record Field(string Element,string Name,string Attribute,string Value,string? TrackGuid,string? EventName,
  int Line,int Column,int? ByteOffset,int? ByteCount,string? RawText);
 internal static object Parse(byte[] bytes)
 {
  using var stream=new MemoryStream(bytes);using var reader=XmlReader.Create(stream,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=32*1024*1024});
  var doc=XDocument.Load(reader,LoadOptions.SetLineInfo);string? text=null;int bom=bytes.AsSpan().StartsWith(new byte[]{239,187,191})?3:0;
  try{if(!bytes.AsSpan().Contains((byte)0))text=new UTF8Encoding(false,true).GetString(bytes,bom,bytes.Length-bom);}catch(DecoderFallbackException){}
  var lines=new List<int>{0};if(text!=null)for(int i=0;i<text.Length;i++)if(text[i]=='\n')lines.Add(i+1);
  Field Attribute(XAttribute a)
  {
   var e=a.Parent!;var info=(IXmlLineInfo)a;int? offset=null,count=null;string? raw=null;
   if(text!=null&&info.HasLineInfo()&&info.LineNumber<=lines.Count)
   {
    int at=lines[info.LineNumber-1]+info.LinePosition-1;
    if(at>=0&&at<text.Length){int equal=text.IndexOf('=',at);if(equal>=at){int q=equal+1;while(q<text.Length&&char.IsWhiteSpace(text[q]))q++;
     if(q<text.Length&&text[q] is '\'' or '"'){int end=text.IndexOf(text[q],q+1);if(end>=0){raw=text[(q+1)..end];offset=bom+Encoding.UTF8.GetByteCount(text.AsSpan(0,q+1));count=Encoding.UTF8.GetByteCount(raw);}}}}
   }
   return new(e.Name.LocalName,e.Attribute("name")?.Value??e.Name.LocalName,a.Name.LocalName,a.Value,
    e.AncestorsAndSelf("Track").FirstOrDefault()?.Attribute("guid")?.Value,e.AncestorsAndSelf("Event").FirstOrDefault()?.Attribute("eventName")?.Value,
    info.LineNumber,info.LinePosition,offset,count,raw);
  }
  var fields=doc.Descendants().SelectMany(e=>e.Attributes()).Select(Attribute).ToArray();
  var tracks=doc.Descendants("Track").Select(t=>new{
   guid=t.Attribute("guid")?.Value,name=t.Attribute("trackName")?.Value,eventType=t.Attribute("eventType")?.Value,enabled=t.Attribute("enabled")?.Value,
   conditions=t.Elements("Condition").Select(c=>c.Attributes().ToDictionary(a=>a.Name.LocalName,a=>a.Value)),
   skinOrAvatarIds=t.Elements("SkinOrAvatarList").Select(e=>e.Attribute("id")?.Value),
   skinFilters=t.Elements("Event").Where(e=>e.Elements("bool").Any(b=>b.Attribute("name")?.Value=="bSelectSkinID"&&b.Attribute("value")?.Value=="true"))
    .SelectMany(e=>e.Elements("Array").Where(a=>a.Attribute("name")?.Value=="skinIDs").SelectMany(a=>a.Elements().Select(v=>v.Attribute("value")?.Value))),
   events=t.Elements("Event").Select(e=>new{name=e.Attribute("eventName")?.Value,attributes=e.Attributes().ToDictionary(a=>a.Name.LocalName,a=>a.Value)})
  }).ToArray();
  return new{type=doc.Root?.Attribute("Type")?.Value,xml=doc.ToString(),fields,tracks,
   references=fields.Where(f=>f.Attribute=="value"&&(f.Element=="String"||f.Value.Contains('/'))).Select(f=>new{field=f.Name,path=f.Value,f.TrackGuid,f.EventName,f.ByteOffset,f.ByteCount,f.RawText,
    evidence="declared XML attribute; resource type and runtime branch applicability require separate resolution",identityConfirmed=false}),
   structureComplete=true,semanticComplete=false,note="Attribute-valued resource paths and action condition edges are retained. Disabled tracks, inverted conditions and skin filters are not treated as unconditional runtime dependencies."};
 }
}
