using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using AssetStudio;
using Hok.Worker;

int passed=0;
void Check(bool condition,string label){if(!condition)throw new Exception(label);passed++;Console.WriteLine("PASS "+label);}
void Reject(Action action,string label){try{action();}catch(Exception e)when(e is InvalidDataException or OverflowException){Check(true,label);return;}throw new Exception("Expected rejection: "+label);}
byte[] Catalog()
{
 using var stream=new MemoryStream();using var w=new BinaryWriter(stream);
 w.Write(new byte[56]);stream.Position=0;w.Write(QtsRoutingCatalog.Magic);w.Write(2);stream.Position=32;w.Write(2);w.Write(2);w.Write(2);stream.Position=56;
 foreach(var name in new[]{"0","3200014006"}){w.Write(name.Length);w.Write(Encoding.ASCII.GetBytes(name));while(stream.Position%8!=0)w.Write((byte)0);w.Write(new byte[8]);}
 w.Write(2);w.Write(10u);w.Write(20u);w.Write(2);w.Write((ushort)1);w.Write(ushort.MaxValue);
 w.Write(2);w.Write((1UL<<32)|20);w.Write((2UL<<32)|20);w.Write(2);w.Write((ushort)0);w.Write((ushort)1);return stream.ToArray();
}
var catalogBytes=Catalog();var catalog=QtsRoutingCatalog.Parse(catalogBytes);
Check(catalog.Resolve(10)?.PackageName=="3200014006","low32 QTS key routes to package");
Check(catalog.Resolve((1UL<<32)|20)?.PackageName=="0"&&catalog.Resolve((2UL<<32)|20)?.PackageName=="3200014006","full64 collision routes remain distinct");
Check(catalog.Resolve((3UL<<32)|20)==null&&catalog.Resolve(11)==null,"unknown QTS key and missing full64 collision are not guessed");
Reject(()=>QtsRoutingCatalog.Parse(catalogBytes[..^1]),"truncated catalog rejected");
Reject(()=>QtsRoutingCatalog.Parse([..catalogBytes,0]),"trailing catalog bytes rejected");
var bad=(byte[])catalogBytes.Clone();BinaryPrimitives.WriteUInt16LittleEndian(bad.AsSpan(bad.Length-2),3);Reject(()=>QtsRoutingCatalog.Parse(bad),"out-of-range package index rejected");
byte[] Script()
{
 using var stream=new MemoryStream();using var w=new BinaryWriter(stream);w.Write(new byte[32]);
 foreach(var values in new[]{Array.Empty<string>(),[],new[]{"Hero_Show(Clone)"},new[]{"SN:Shader/Effect,KN:KEY,PT:0\n"},new[]{"Shader/Effect|KEY|0|1,2,3"},[]})
 {w.Write(values.Length);foreach(var value in values){var bytes=Encoding.UTF8.GetBytes(value);w.Write(bytes.Length);w.Write(bytes);while(stream.Position%4!=0)w.Write((byte)0);}}
 return stream.ToArray();
}
var script=Script();var scriptResult=JsonSerializer.SerializeToElement(HokScriptSchemas.Parse(script,32));
Check(scriptResult.GetProperty("structureComplete").GetBoolean()&&!scriptResult.GetProperty("semanticComplete").GetBoolean(),"validated script boundaries are not full semantic coverage");
Check(scriptResult.GetProperty("variants")[0].GetProperty("Shader").GetString()=="Shader/Effect","shader/keyword/pass text has explicit typed fields");
Reject(()=>HokScriptSchemas.Parse(script[..^1],32),"truncated script cannot be marked complete");
bad=(byte[])script.Clone();BinaryPrimitives.WriteInt32LittleEndian(bad.AsSpan(32),1);Check(JsonSerializer.SerializeToElement(HokScriptSchemas.Parse(bad,32)).GetProperty("fields").GetProperty("m_skinType").GetInt32()==1,"TTre-proven scalar field is not misread as a vector count");
byte[] Stdr()
{
 using var stream=new MemoryStream();using var w=new BinaryWriter(stream);w.Write(new byte[152]);stream.Position=0;w.Write("stdr"u8);stream.Position=8;w.Write(2);w.Write("MSES"u8);w.Write(8);w.Write(8);w.Write(1);stream.Position=76;w.Write("UTF-8\0"u8);stream.Position=144;w.Write(160);stream.Position=152;w.Write(14006);w.Write(99);
 foreach(var s in new[]{"赤影疾锋","Prefab/14006"}){w.Write(new byte[8]);w.Write(Encoding.UTF8.GetBytes(s));w.Write((byte)0);while(stream.Position%4!=0)w.Write((byte)0);}
 return stream.ToArray();
}
var stdr=Stdr();var table=StdrTable.Parse(stdr);
Check(table.RecordCount==1&&table.Records[0].FirstWord==14006&&table.Strings[1].Text=="Prefab/14006"&&table.StructureComplete&&!table.SemanticComplete,"stdr record and UTF-8 pool boundaries do not imply field semantics");
Reject(()=>StdrTable.Parse(stdr[..^1]),"truncated stdr padding rejected");
bad=(byte[])stdr.Clone();BinaryPrimitives.WriteInt32LittleEndian(bad.AsSpan(144),159);Reject(()=>StdrTable.Parse(bad),"stdr record span mismatch rejected");
Reject(()=>StdrTable.Parse([..stdr,0]),"stdr undeclared trailing bytes rejected");
var xml=Encoding.UTF8.GetBytes("<Project>\n<Track guid=\"t\" enabled=\"false\"><Condition guid=\"filter\" status=\"false\"/><Event eventName=\"TriggerParticle\"><String name=\"resourceName\" value=\"特效/a&amp;b\"/></Event></Track></Project>");
var xmlDoc=JsonSerializer.SerializeToElement(HokXmlData.Parse(xml),new JsonSerializerOptions(JsonSerializerDefaults.Web));var xmlRef=xmlDoc.GetProperty("references")[0];
Check(xmlRef.GetProperty("path").GetString()=="特效/a&b"&&Encoding.UTF8.GetString(xml.AsSpan(xmlRef.GetProperty("byteOffset").GetInt32(),xmlRef.GetProperty("byteCount").GetInt32()))=="特效/a&amp;b","XML attribute paths preserve decoded value and exact original UTF-8 byte range");
Check(xmlDoc.GetProperty("tracks")[0].GetProperty("enabled").GetString()=="false"&&xmlDoc.GetProperty("tracks")[0].GetProperty("conditions")[0].GetProperty("status").GetString()=="false","disabled tracks and inverted conditions are not erased");
QtsKeyValueDatabase Mapping(byte[] key,byte[] value)
{
 var db=new QtsKeyValueDatabase();db.Records.Add(new(100,-1,28+key.Length+value.Length,128+value.Length,128,key,value));return db;
}
byte[] Words(params long[] values){var raw=new byte[8*values.Length];for(int i=0;i<values.Length;i++)BinaryPrimitives.WriteInt64LittleEndian(raw.AsSpan(i*8),values[i]);return raw;}
var mapping=Mapping(Words(-1),Words(long.MinValue,-3,9007199254740993L));mapping.Records.Add(mapping.Records[0]);
var mapped=QtsResourceMetadata.Resources(mapping);
Check(mapped.Length==2&&mapped[0].ResourceKey=="18446744073709551615"&&mapped[0].BlobId=="9223372036854775808"&&mapped[0].PathIds.SequenceEqual(new[]{"-3","9007199254740993"}),"resource maps retain unsigned blob IDs, signed 64-bit PathIDs, multiple targets and duplicate records without Number rounding");
Reject(()=>QtsResourceMetadata.Resources(Mapping(Words(1),new byte[17])),"misaligned resource map value rejected");
Reject(()=>QtsResourceMetadata.Resources(Mapping(Words(1),Words(2))),"resource map without object PathID rejected");
var scripts=Mapping(Words(5,-6),Words(-7,8));scripts.Records.Add(new(200,-1,44,236,228,Words(9),Words(10)));
var scriptMapping=QtsResourceMetadata.Scripts(scripts);
Check(scriptMapping.Records.Length==1&&scriptMapping.Records[0].PathId=="-6"&&scriptMapping.Records[0].ScriptPathIds.SequenceEqual(new[]{"-7","8"})&&scriptMapping.UninterpretedRecords.Length==1,"script mappings separate typed object dependencies from uninterpreted singleton metadata");
Reject(()=>QtsResourceMetadata.Scripts(Mapping(Words(1,2),new byte[9])),"misaligned script dependency value rejected");
byte[] Npy()
{
 using var stream=new MemoryStream();using var w=new BinaryWriter(stream);w.Write(new byte[]{147,78,85,77,80,89,1,0});
 var header=Encoding.ASCII.GetBytes("{'descr': '<f2', 'fortran_order': False, 'shape': (2,), }\n");w.Write((ushort)header.Length);w.Write(header);
 w.Write(BitConverter.HalfToUInt16Bits((System.Half)1.5));w.Write(BitConverter.HalfToUInt16Bits((System.Half)(-2)));return stream.ToArray();
}
var npy=Npy();using(var input=new MemoryStream(npy))
{var h=NumpyData.ReadHeader(input,npy.Length);Check(h.Elements==2&&h.ItemBytes==2&&h.DataBytes==4&&h.Shape.SequenceEqual(new long[]{2}),"NPY half-float shape/dtype covers exact payload");}
var npyView=JsonSerializer.SerializeToElement(NumpyData.Describe(()=>new MemoryStream(npy),npy.Length));
Check(npyView.GetProperty("sampleValues")[0].GetDouble()==1.5&&npyView.GetProperty("sampleValues")[1].GetDouble()==-2,"NPY preview decodes actual half-float values");
using(var input=new MemoryStream(npy[..^1]))Reject(()=>NumpyData.ReadHeader(input,npy.Length-1),"NPY truncated array is not complete");
using(var input=new MemoryStream([..npy,0]))Reject(()=>NumpyData.ReadHeader(input,npy.Length+1),"NPY trailing bytes rejected");
byte[] Npz()
{using var stream=new MemoryStream();using(var zip=new System.IO.Compression.ZipArchive(stream,System.IO.Compression.ZipArchiveMode.Create,true))
 {using var member=zip.CreateEntry("param_0.npy").Open();member.Write(npy);}return stream.ToArray();}
var npz=Npz();var arrays=NumpyData.ReadArchive(()=>new MemoryStream(npz));
Check(arrays.Length==1&&arrays[0].Header.Elements==2,"NPZ indexes members without loading array values");
using(var member=NumpyData.OpenMember(()=>new MemoryStream(npz),arrays[0].Name,arrays[0].Bytes))
{var copy=new byte[member.Length];member.ReadExactly(copy);Check(copy.SequenceEqual(npy),"NPZ lazy member preserves original NPY bytes");}
ResourceAsset.ResetMetrics();var arrayBacking=new ContainerEntry("parameter","fixture",()=>NumpyData.OpenMember(()=>new MemoryStream(npz),arrays[0].Name,arrays[0].Bytes),arrays[0].Bytes,"NumpyArray");
var arrayAsset=new ResourceAsset("parameter","param_0.npy","fixture","NumpyArray","npy",[],Backing:arrayBacking);
var arrayView=JsonSerializer.SerializeToElement(HokStructured.Parse(arrayAsset,true));
Check(arrayAsset.Formats.Contains("original")&&arrayAsset.Formats.Contains("json")&&ResourceAsset.MaterializedBytes==0&&arrayView.GetProperty("header").GetProperty("Descriptor").GetString()=="<f2","array preview is bounded and does not materialize the whole archive");
ZipContainerTests.Run(Check,Reject,args);
if(args.Length>0)
{
 string probes=Path.Combine(args[0],"probes");
 // Numeric probe directories are extracted archive fixtures; zip-worker-* holds test output.
 foreach(var archiveProbe in Directory.EnumerateDirectories(probes,"zip-*").Where(p=>System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(p),@"^zip-\d+$")))
 {
  var payload=Directory.GetFiles(archiveProbe,"*.payload").Single();using var archive=System.IO.Compression.ZipFile.OpenRead(payload);
  if(archive.Entries.Any(e=>!e.FullName.EndsWith(".npy",StringComparison.OrdinalIgnoreCase)))
  {Reject(()=>NumpyData.ReadArchive(()=>File.OpenRead(payload)),"unrelated APK/ZIP cannot be mislabeled a numeric NPZ archive");continue;}
  var members=NumpyData.ReadArchive(()=>File.OpenRead(payload));
  Check(members.Length>0&&members.All(m=>m.Header.Elements>=0),"real extracted NPZ members have exact bounded headers: "+Path.GetFileName(archiveProbe));
 }
 var typeBytes=File.ReadAllBytes(Path.Combine(probes,"nested-18078322517950175870","18078322517950175870.payload"));
 var kv=QtsKeyValueDatabase.Parse(typeBytes);var trees=QtsTypeTreeDatabase.Parse(kv);
 Check(kv.Records.Count==1398&&trees.Schemas.Count==1398,"all nested TTre records parsed without dropping linked records");
 var scriptTree=trees.Schemas.Single(s=>s.Hash=="14B8A76D7AC245084ABCE149618CFB17").Tree;
 Check(scriptTree.m_Nodes.Any(n=>n.m_Name=="m_PrefabPath")&&scriptTree.m_Nodes.Any(n=>n.m_Name=="m_PSOsData"),"package type hash supplies original script field names");
 var schemaPath=Path.Combine(probes,"nested-18078322517950175870","18078322517950175870.payload");
 var managedProbe=Path.Combine(probes,"managed-ref-380007851652318514","380007851652318514.payload");
 if(File.Exists(managedProbe))
 {
  var managedManager=new AssetsManager{Game=GameManager.GetGame(GameType.HonorOfKings),DeferHeavyObjects=true};managedManager.LoadQtsTypeDatabase(schemaPath);managedManager.LoadFilesReadOnly(managedProbe);
  var managedObject=managedManager.assetsFileList.Single().Objects.Single();var managedStatus=managedObject.assetsFile.ParseStatuses[managedObject.m_PathID];
  Check(managedStatus.Status=="typed-complete"&&managedStatus.RemainingBytes==0,"managed animation blueprint consumes all 4056 bytes: "+managedStatus.Error);
  var managedFields=managedObject.ToType();var registry=(System.Collections.Specialized.OrderedDictionary)managedFields["references"]!;
  var records=(List<object>)registry["RefIds"]!;
  Check(records.Count>0&&records.Cast<System.Collections.Specialized.OrderedDictionary>().Any(r=>r["data"] is System.Collections.Specialized.OrderedDictionary d&&d.Count>0),"managed registry contains typed dynamic node fields, not empty placeholders");
  var managedIssues=new List<string>();var managedRefs=ReferenceCollector.Collect(managedObject,managedIssues);
  Check(managedIssues.Count==0&&managedRefs.Any(r=>r.Field.Contains("references.RefIds[")),"managed registry nested Unity references retain their field paths");
  var selectedType=managedObject.assetsFile.m_RefTypes.First(t=>t.m_KlassName=="FloatParameter");var originalTree=selectedType.m_Type;selectedType.m_Type=null;
  Reject(()=>managedObject.ToType(),"missing managed-reference schema fails explicitly instead of consuming zero bytes");selectedType.m_Type=originalTree;
  managedObject.assetsFile.m_RefTypes.Add(selectedType);Reject(()=>managedObject.ToType(),"ambiguous managed type name/namespace/assembly is not first-match-wins");managedObject.assetsFile.m_RefTypes.RemoveAt(managedObject.assetsFile.m_RefTypes.Count-1);
  Check(managedObject.Dump().Contains("FloatParameter"),"managed blueprint preview uses the same dynamic reader as export");managedManager.Clear();
 }
 var semanticManager=new AssetsManager{Game=GameManager.GetGame(GameType.HonorOfKings),DeferHeavyObjects=true};
 semanticManager.LoadQtsTypeDatabase(schemaPath);
 semanticManager.LoadFilesReadOnly(Path.Combine(probes,"14006-prefab","1403202040702869312.payload"));
 var fullScript=semanticManager.assetsFileList.SelectMany(f=>f.Objects).Single(o=>o.m_PathID==7765958166190762939);
 var scriptStatus=fullScript.assetsFile.ParseStatuses[fullScript.m_PathID];
 Check(fullScript.UseTypeTree&&scriptStatus.Status=="typed-complete"&&scriptStatus.RemainingBytes==0,"real 521532-byte script fully consumes exact package schema");
 var fullFields=fullScript.ToType();Check(fullFields.Contains("m_skinType")&&fullFields.Contains("m_PSOsData")&&fullFields.Contains("m_IsRFCfgOverride"),"original script fields survive structured export");
 var observedStrings=new List<(string Field,string Value,long Offset)>();var stringIssues=new List<string>();
 ReferenceCollector.Collect(fullScript,stringIssues,(field,value,offset)=>observedStrings.Add((field,value,offset)));
 Check(stringIssues.Count==0&&observedStrings.Any(s=>s.Field=="m_PathsData[0]"&&s.Value.Length>0)&&observedStrings.All(s=>s.Offset>=fullScript.reader.byteStart&&s.Offset<fullScript.reader.byteStart+fullScript.byteSize),"validated script string fields retain names and bounded byte provenance");
 semanticManager.Clear();
 var trailManager=new AssetsManager{Game=GameManager.GetGame(GameType.HonorOfKings),DeferHeavyObjects=true};trailManager.LoadQtsTypeDatabase(schemaPath);
 trailManager.LoadFilesReadOnly(Path.Combine(probes,"2-aratrail","10065131995738856907.payload"));
 var trail=trailManager.assetsFileList.SelectMany(f=>f.Objects).Single(o=>(int)o.type==366);
 Check(trail.UseTypeTree&&trail.assetsFile.ParseStatuses[trail.m_PathID].RemainingBytes==0&&trail.type.ToString()=="AraTrail","class 366 is a fully decoded AraTrail component, not an unknown raw asset");trailManager.Clear();
 if(args.Length>1&&args[1]=="dependencies")
 {
  var dependencyManager=new AssetsManager{Game=GameManager.GetGame(GameType.HonorOfKings),DeferHeavyObjects=true};
  var corpus=Path.GetFullPath(Path.Combine(args[0],"../../../all"));dependencyManager.SetQtsDependencySources([Path.Combine(corpus,"0/0_0.db"),Path.Combine(corpus,"0/0.db")]);
  dependencyManager.LoadFilesReadOnly(Path.Combine(probes,"14006-prefab","1403202040702869312.payload"));
  var dependencyScript=dependencyManager.assetsFileList.SelectMany(f=>f.Objects).Single(o=>o.m_PathID==7765958166190762939);
  Check(dependencyScript.UseTypeTree&&dependencyManager.QtsTypeSchemaSources.Length==1&&dependencyManager.QtsDatabases.Count==0,"workspace dependency schemas are manifest-verified and loaded without opening all dependency objects");dependencyManager.Clear();
 }
 Reject(()=>QtsTypeTreeDatabase.ReadTree(kv.Records[0].Value.Span[..^1]),"truncated type tree rejected");
 bad=(byte[])typeBytes.Clone();BinaryPrimitives.WriteInt32LittleEndian(bad.AsSpan(kv.Records[0].Offset+20),int.MaxValue);Reject(()=>QtsKeyValueDatabase.Parse(bad),"oversized KV key rejected");
 bad=(byte[])typeBytes.Clone();BinaryPrimitives.WriteInt32LittleEndian(bad.AsSpan(kv.Records[0].Offset),0);Reject(()=>QtsKeyValueDatabase.Parse(bad),"KV self-offset corruption rejected");
 bad=(byte[])typeBytes.Clone();BinaryPrimitives.WriteInt32LittleEndian(bad.AsSpan(kv.Records[0].Offset+4),kv.Records[0].Offset);Reject(()=>QtsKeyValueDatabase.Parse(bad),"KV linked-record cycle rejected rather than silently deduplicated");
 foreach(var id in new[]{"6167850575042422745","2254348891505130285","8071403838198930324"})
 {var nested=QtsKeyValueDatabase.Parse(File.ReadAllBytes(Path.Combine(probes,"nested-"+id,id+".payload")));Check(nested.Records.Count>30000,"nested mapping records retained: "+id);}
 var resourceMap=QtsResourceMetadata.Resources(QtsKeyValueDatabase.Parse(File.ReadAllBytes(Path.Combine(probes,"nested-6167850575042422745","6167850575042422745.payload"))));
 Check(resourceMap.Length==299589&&resourceMap.Any(r=>r.BlobId=="1403202040702869312"&&r.PathIds.Contains("9215514451740468560")),"real ResEntries mapping links the 14006 SHOW object to its exact QTS entry and PathID");
 var scriptMap=QtsResourceMetadata.Scripts(QtsKeyValueDatabase.Parse(File.ReadAllBytes(Path.Combine(probes,"nested-2254348891505130285","2254348891505130285.payload"))));
 Check(scriptMap.Records.Length==124601&&scriptMap.Records.Sum(r=>r.ScriptPathIds.Length)==348092&&scriptMap.UninterpretedRecords.Length==1,"all real script dependencies and the uninterpreted metadata record survive typed decoding");
 var mapAsset=new ResourceAsset("test","ResEntriesDB.db","fixture","QtsResourceMap","db",File.ReadAllBytes(Path.Combine(probes,"nested-6167850575042422745","6167850575042422745.payload")));
 var preview=JsonSerializer.SerializeToElement(HokStructured.Parse(mapAsset,true),new JsonSerializerOptions(JsonSerializerDefaults.Web));
 Check(mapAsset.IsStructured&&mapAsset.Formats.Contains("json")&&preview.GetProperty("recordCount").GetInt32()==299589&&preview.GetProperty("records").GetArrayLength()==256&&preview.GetProperty("truncated").GetBoolean(),"mapping preview is bounded and explicitly truncated while full JSON export remains available");
 foreach(var specimen in new[]{("14006-prefab","1403202040702869312.payload",185472,521532),("14005-prefab","1992855844638804839.payload",140888,548440)})
 {
  var data=File.ReadAllBytes(Path.Combine(probes,specimen.Item1,specimen.Item2)).AsSpan(specimen.Item3,specimen.Item4).ToArray();
  var parsed=JsonSerializer.SerializeToElement(HokScriptSchemas.Parse(data,32));Check(parsed.GetProperty("bytes").GetInt32()==data.Length-32,specimen.Item1+" exact script table boundaries");
 }
 foreach(var id in new[]{"18748427691075818","913661504037930558","2575805863185491323"})
 {
  var data=File.ReadAllBytes(Path.Combine(probes,"9-"+id,id+".payload"));var parsed=StdrTable.Parse(data);
  Check(parsed.Strings[^1].EndOffset==data.Length&&parsed.Records.Count==parsed.RecordCount,"real stdr table has exact record/string closure: "+id);
  Check(parsed.Records.Sum(r=>r.StringReferences.Count)>1000,"fingerprinted stdr handles point to validated pool boundaries: "+id);
  var first=parsed.Records.First(r=>r.StringReferences.Count>0).StringReferences[0];var damaged=(byte[])data.Clone();BinaryPrimitives.WriteUInt32LittleEndian(damaged.AsSpan(first.AbsoluteOffset+4),1);
  Reject(()=>StdrTable.Parse(damaged),"invalid stdr handle cannot bind a nearby string: "+id);
  if(id=="2575805863185491323")Check(parsed.Records.Single(r=>r.FirstWord==14006).StringReferences.Any(f=>f.Text=="Hero_GuanYu_Skin_C"),"14006 record reaches its own skin alias through exact field offsets");
 }
}
Console.WriteLine(JsonSerializer.Serialize(new{passed}));
