using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
namespace Hok.Worker;

// These are non-Unity entries found in the fixed corpus. Detection validates
// structure and bounds; filenames and hero/skill keywords are never evidence.
internal static class HokStructured
{
 static readonly UTF8Encoding Utf8=new(false,true);
 static uint U32(ReadOnlySpan<byte> b,int p)=>BinaryPrimitives.ReadUInt32LittleEndian(b[p..]);
 public static (string type,string extension)? Detect(byte[] head,long length){
  var b=head.AsSpan();
  if(AssetStudio.QtsKeyValueDatabase.HasSignature(b))return("QtsKeyValueDatabase","db");
  if(StdrTable.Detect(b,length))return("StdrTable","stdr");
  if(b.Length>=56&&b[..4].SequenceEqual("INGQ"u8)&&U32(b,4)==2)return("QtsRoutingCatalog","catalog");
  if(length==head.Length&&AssetStudio.QtsChecksumManifest.TryParse(b,out _))return("QtsChecksumManifest","txt");
  if(b.Length>=20&&U32(b,0)==length&&U32(b,4)==8&&b.Slice(8,4).SequenceEqual("root"u8))return("HokObjectTree","hoktree");
  if(b.Length>=20&&U32(b,0)==1001&&U32(b,4)==length)return("HokActionTimeline","action");
  if(b.StartsWith(new byte[]{239,187,191}))b=b[3..];
  if(b.StartsWith("<?xml"u8)||b.StartsWith("<Root "u8))return("XmlAsset","xml");
  if(length<=head.Length&&b.Length>0){try{string text=Utf8.GetString(b);if(text.All(c=>!char.IsControl(c)||c is '\r' or '\n' or '\t'))return("TextFile","txt");}catch(DecoderFallbackException){}}
  return null;
 }
 public static object Parse(ResourceAsset asset,bool preview=false)=>asset.Type switch{
  "ZipArchive" or "AndroidPackage"=>AssetStudio.ZipContainerData.Describe(asset.Open),
  "NumpyArray"=>AssetStudio.NumpyData.Describe(asset.Open,asset.Length),
  "NumpyArchive"=>new{schema="npz-numeric-v1",arrays=AssetStudio.NumpyData.ReadArchive(asset.Open),structureComplete=true,note="Archive and individual NPY arrays remain available in their native formats. Numeric values are loaded only when an array is previewed/exported."},
  "HokObjectTree"=>new TreeReader(asset.Data).Parse(),
  "StdrTable"=>StdrTable.Parse(asset.Data),
  "QtsKeyValueDatabase"=>ParseKeyValues(asset,preview?256:int.MaxValue),
  "QtsTypeTreeDatabase"=>ParseTypeTrees(asset,preview?16:int.MaxValue),
  "QtsResourceMap"=>ParseResourceMap(asset,preview?256:int.MaxValue),
  "QtsScriptDependencies"=>ParseScriptDependencies(asset,preview?256:int.MaxValue),
  "QtsRoutingCatalog"=>AssetStudio.QtsRoutingCatalog.Parse(asset.Data).Summary(),
  "HokActionTimeline"=>ParseAction(asset.Data),
  "XmlAsset"=>ParseXml(asset.Data),
  "TextFile"=>new{text=Utf8.GetString(asset.Data)},
  "QtsChecksumManifest"=>AssetStudio.QtsChecksumManifest.TryParse(asset.Data,out var records)
   ?new{schema="qts-companion-checksums-v1",records,checksumAlgorithm="unconfirmed",identityDomain="companion filenames; not Unity GUIDs or object PathIDs"}
   :throw new InvalidDataException("Invalid checksum manifest"),
  _=>throw new NotSupportedException()
 };
 static object ParseKeyValues(ResourceAsset asset,int limit){var db=AssetStudio.QtsKeyValueDatabase.Parse(asset.Data);return new{schema="qts-key-value-v1",db.Bytes,db.Pages,recordCount=db.Records.Count,truncated=db.Records.Count>limit,shownRecords=Math.Min(limit,db.Records.Count),structureComplete=true,semanticComplete=false,records=db.Records.Take(limit).Select(r=>new{r.Offset,r.Next,r.RecordBytes,r.KeyOffset,r.ValueOffset,key=Convert.ToHexString(r.Key.Span),value=Convert.ToHexString(r.Value.Span)}),note="Key/value byte ranges are exact. Preview may be limited; JSON export retains every record. Mapping domains require independent evidence; keys are not implicitly Unity PathIDs."};}
 static object ParseTypeTrees(ResourceAsset asset,int limit){var db=AssetStudio.QtsTypeTreeDatabase.Parse(AssetStudio.QtsKeyValueDatabase.Parse(asset.Data));return new{schema="qts-type-tree-v1",schemaCount=db.Schemas.Count,truncated=db.Schemas.Count>limit,schemas=db.Schemas.Take(limit).Select(s=>new{s.Hash,s.PayloadSha256,s.RecordOffset,nodes=s.Tree.m_Nodes.Select(n=>new{type=n.m_Type,name=n.m_Name,level=n.m_Level,bytes=n.m_ByteSize,index=n.m_Index,flags=n.m_MetaFlag,version=n.m_Version,refTypeHash=n.m_RefTypeHash.ToString()})})};}
 static object ParseResourceMap(ResourceAsset asset,int limit)
 {
  var db=AssetStudio.QtsKeyValueDatabase.Parse(asset.Data);var records=AssetStudio.QtsResourceMetadata.Resources(db);
  return new{schema="qts-resource-map-v1",recordCount=records.Length,truncated=records.Length>limit,structureComplete=true,semanticComplete=false,
   records=records.Take(limit),identityDomain="Resource key -> blob ID + object PathIDs. Blob may be a direct QTS entry or require a location-table lookup; none of these fields is a Unity external GUID.",
   note="Original record order, duplicate resource keys and every PathID are retained. Path spelling/hash normalization and runtime branch closure are not inferred."};
 }
 static object ParseScriptDependencies(ResourceAsset asset,int limit)
 {
  var db=AssetStudio.QtsKeyValueDatabase.Parse(asset.Data);var table=AssetStudio.QtsResourceMetadata.Scripts(db);
  return new{schema="qts-script-dependencies-v1",recordCount=db.Records.Count,truncated=table.Records.Length>limit,structureComplete=true,semanticComplete=false,
   records=table.Records.Take(limit),uninterpretedRecords=table.UninterpretedRecords,
   identityDomain="Blob ID + object PathID -> MonoScript PathIDs. The separate eight-byte-key record has unknown semantics and is retained explicitly."};
 }
 public static object ParseXml(byte[] bytes)=>HokXmlData.Parse(bytes);
 public static void Write(ResourceAsset asset,string format,string output){
  var data=Parse(asset);if(format=="json"){File.WriteAllText(output,JsonSerializer.Serialize(data,new JsonSerializerOptions(Program.Json){WriteIndented=true}));return;}
  if(format=="xml"&&asset.Type=="HokObjectTree"){var node=(TreeNode)data;XElement Xml(TreeNode n)=>new("Node",new XAttribute("name",n.Name),n.Attributes.Select(a=>new XElement("Attribute",new XAttribute("name",a.Name),new XAttribute("encoding",a.Value.Text is null?"hex":"text"),a.Value.Text??a.Value.Hex)),new XElement("Content",new XAttribute("encoding",n.Content.Text is null?"hex":"text"),n.Content.Text??n.Content.Hex),n.Children.Select(Xml));new XDocument(Xml(node)).Save(output);return;}
  throw new NotSupportedException(format);
 }
 internal sealed record Value(string? Text,string Hex,int[]? Int32Words=null);
 internal sealed record Attribute(string Name,Value Value);
 internal sealed record TreeNode(string Name,List<Attribute> Attributes,Value Content,List<TreeNode> Children);
 sealed class TreeReader(byte[] bytes){
  int nodes;
  static int Align(int p)=>checked((p+3)&~3);
  void Range(int p,int n,int end){if(p<0||n<0||p>end-n)throw new InvalidDataException($"HOK tree range {p}+{n}/{end}");}
  (int begin,int end,int next) Block(int p,int end){Range(p,4,end);int size=checked((int)U32(bytes,p));if(size<4)throw new InvalidDataException("HOK tree block too short");Range(p,size,end);return(p+4,p+size,Align(p+size));}
  (Value value,int next) ReadValue(int p,int end){var b=Block(p,end);var raw=bytes.AsSpan(b.begin,b.end-b.begin);string? text=null;try{var s=Utf8.GetString(raw);if(s.All(c=>!char.IsControl(c)||c is '\r' or '\n' or '\t'))text=s;}catch(DecoderFallbackException){}int[]? words=text is null&&raw.Length==12?new[]{BinaryPrimitives.ReadInt32LittleEndian(raw),BinaryPrimitives.ReadInt32LittleEndian(raw[4..]),BinaryPrimitives.ReadInt32LittleEndian(raw[8..])}:null;return(new(text,Convert.ToHexString(raw),words),b.next);}
  (TreeNode node,int next) Node(int p,int end,int depth){if(depth>128||++nodes>200000)throw new InvalidDataException("HOK tree nesting/node limit");var b=Block(p,end);var name=ReadValue(b.begin,b.end);if(name.value.Text is not{} title||title.Length==0)throw new InvalidDataException("Invalid HOK node name");p=name.next;var ab=Block(p,b.end);p=ab.begin;int count=0;if(p<ab.end){Range(p,4,ab.end);count=checked((int)U32(bytes,p));p+=4;}if(count<0||count>(ab.end-p)/12)throw new InvalidDataException("Invalid attribute count");var attributes=new List<Attribute>();for(int i=0;i<count;i++){var a=Block(p,ab.end);var k=ReadValue(a.begin,a.end);var v=ReadValue(k.next,a.end);if(k.value.Text is null||v.next!=Align(a.end))throw new InvalidDataException("Invalid HOK attribute");attributes.Add(new(k.value.Text,v.value));p=a.next;}if(p!=Align(ab.end))throw new InvalidDataException("Attribute table trailing bytes");var content=ReadValue(p,b.end);var cb=Block(content.next,b.end);p=cb.begin;count=0;if(p<cb.end){Range(p,4,cb.end);count=checked((int)U32(bytes,p));p+=4;}if(count<0||count>(cb.end-p)/16)throw new InvalidDataException("Invalid child count");var children=new List<TreeNode>();for(int i=0;i<count;i++){var child=Node(p,cb.end,depth+1);children.Add(child.node);p=child.next;}if(p!=Align(cb.end)||p!=Align(b.end))throw new InvalidDataException("HOK node trailing bytes");return(new(title,attributes,content.value,children),b.next);}
  public TreeNode Parse(){var r=Node(0,bytes.Length,0);if(r.next!=bytes.Length||r.node.Name!="root")throw new InvalidDataException("Invalid HOK object tree root/length");return r.node;}
 }
 static object ParseAction(byte[] bytes){
  if(bytes.Length<20||U32(bytes,0)!=1001||U32(bytes,4)!=bytes.Length)throw new InvalidDataException("Invalid HOK action header");int p=20,count=checked((int)U32(bytes,16));if(count<0||count>bytes.Length/8)throw new InvalidDataException("Invalid actor table count");var actors=new List<object>();
  for(int i=0;i<count;i++){if(p>bytes.Length-4)throw new EndOfStreamException();int n=checked((int)U32(bytes,p));p+=4;if(n<0||p>bytes.Length-n-4)throw new InvalidDataException("Actor name range");string name=Utf8.GetString(bytes,p,n);p+=n;uint id=U32(bytes,p);p+=4;actors.Add(new{name,id});}
  int actorEnd=p;
  void Need(int length){if(length<0||p>bytes.Length-length)throw new InvalidDataException("Truncated action table at "+p);}
  uint Word(){Need(4);uint value=U32(bytes,p);p+=4;return value;}
  string String(){int size=checked((int)Word());Need(size);var text=Utf8.GetString(bytes,p,size);p+=size;return text;}
  var variables=new List<object>();uint variableCount=Word();if(variableCount>(bytes.Length-p)/12)throw new InvalidDataException("Invalid action variable count");
  for(int i=0;i<variableCount;i++){
   int at=p;string name=String();uint hash=Word();object value;string encoding;
   if(hash==0x72f31961){Need(12);value=new[]{BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(p)),BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(p+4)),BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(p+8))};encoding="int32-vector3 (scale unspecified)";p+=12;}
   else if(hash is 0x738fb290 or 0x798c7be8){int dimensions=hash==0x738fb290?3:4;Need(dimensions*4);var components=Enumerable.Range(0,dimensions).Select(i=>BitConverter.ToSingle(bytes,p+i*4)).ToArray();if(!components.All(float.IsFinite))throw new InvalidDataException("Non-finite action vector");value=components;encoding="float32-vector"+dimensions;p+=dimensions*4;}
   else if(hash==0x7a7225b8){value=String();encoding="utf8-string";}
   else return new{version=1001,totalBytes=bytes.Length,headerWords=new[]{U32(bytes,8),U32(bytes,12)},actors,variables,parseStatus="typed-partial",structureComplete=false,unparsedOffset=at,remainingBytes=bytes.Length-at,reason=$"Unsupported variable type hash 0x{hash:X8}; no speculative event offsets used.",embeddedLengthPrefixedStrings=ActionStrings(bytes,actorEnd,bytes.Length),bodyHex=Convert.ToHexString(bytes.AsSpan(at))};
   variables.Add(new{name,typeHash=$"0x{hash:X8}",offset=at,bytes=p-at,encoding,value});
  }
  uint durationTicks=Word();Need(1);byte flags=bytes[p++];uint eventCount=Word();if(eventCount>(bytes.Length-p-4)/8)throw new InvalidDataException("Invalid action event count");
  var events=new List<object>();for(int i=0;i<eventCount;i++){
   uint hash=Word();int size=checked((int)Word());Need(size);int at=p;p+=size;
   events.Add(new{index=i,typeHash=$"0x{hash:X8}",offset=at,endOffset=p,bytes=size,
    sha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes.AsSpan(at,size))),
    embeddedLengthPrefixedStrings=ActionStrings(bytes,at,p),parametersHex=Convert.ToHexString(bytes.AsSpan(at,size)),parameterStatus="schema-unresolved"});
  }
  if(Word()!=0x1234||p!=bytes.Length)throw new InvalidDataException("Invalid action terminal marker or trailing bytes");
  return new{version=1001,totalBytes=bytes.Length,headerWords=new[]{U32(bytes,8),U32(bytes,12)},actors,variables,durationTicks,timeUnit="unconfirmed",flags,events,structureComplete=true,structureBytes=p,parseStatus="typed-partial",note="Actor/variable/event tables and terminal marker validated. Event type hashes and unconfirmed parameter layouts are retained explicitly; this is not a complete runtime simulation.",embeddedLengthPrefixedStrings=ActionStrings(bytes,actorEnd,bytes.Length)};
 }
 static List<object> ActionStrings(byte[] bytes,int begin,int end){
  var strings=new List<object>();for(int i=begin;i<=end-8;i++){uint n=U32(bytes,i);if(n<4||n>4096||n>end-i-4)continue;try{string value=Utf8.GetString(bytes,i+4,(int)n);if(value.All(c=>!char.IsControl(c))){strings.Add(new{offset=i+4,bytes=n,value,evidence="unverified-string-hint",isConfirmedReference=false});i+=3+(int)n;}}catch(DecoderFallbackException){}}return strings;
 }
}
