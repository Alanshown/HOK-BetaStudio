using AssetStudio;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

// Optional second argument: complete 3200019306 sample directory. No test mutates input.
string root=Path.GetFullPath(args[0]);
string scratch=Path.Combine(root,".cache","discovery-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(scratch);
var checks=new List<string>();
void Check(bool ok,string message){if(!ok)throw new Exception(message);checks.Add(message);Console.WriteLine("PASS "+message);}
AssetsManager Manager()=>new(){Game=GameManager.GetGame(GameType.HonorOfKings)};
Logger.Silent=true;
byte[] Text(string name){using var ms=new MemoryStream();using var w=new BinaryWriter(ms);var bytes=Encoding.UTF8.GetBytes(name);w.Write(bytes.Length);w.Write(bytes);while(ms.Position%4!=0)w.Write((byte)0);w.Write(5);w.Write("hello"u8);return ms.ToArray();}
byte[] Serialized(params (int Class,long Id,byte[] Data)[] objects){
 using var meta=new MemoryStream();using var w=new BinaryWriter(meta);using var raw=new MemoryStream();
 w.Write("2018.4.0f1\0"u8);w.Write(13);w.Write(false);w.Write(objects.Length);
 foreach(var o in objects){w.Write(o.Class);w.Write(false);w.Write((short)-1);if(o.Class==114)w.Write(new byte[16]);w.Write(new byte[16]);w.Write(0);}
 w.Write(objects.Length);
 for(int i=0;i<objects.Length;i++){var o=objects[i];while((20+meta.Position)%4!=0)w.Write((byte)0);while(raw.Position%4!=0)raw.WriteByte(0);w.Write(o.Id);w.Write((uint)raw.Position);w.Write((uint)o.Data.Length);w.Write(i);raw.Write(o.Data);}
 w.Write(0);w.Write(0);w.Write((byte)0);int offset=(20+(int)meta.Length+15)/16*16;var result=new byte[offset+raw.Length];
 BinaryPrimitives.WriteUInt32BigEndian(result,(uint)meta.Length);BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(4),(uint)result.Length);BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(8),17);BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(12),(uint)offset);meta.ToArray().CopyTo(result,20);raw.ToArray().CopyTo(result,offset);return result;
}
string Write(string name,byte[] data){string f=Path.Combine(scratch,name);Directory.CreateDirectory(Path.GetDirectoryName(f)!);File.WriteAllBytes(f,data);return f;}
var broken=Write("broken.assets",Serialized((74,1,new byte[4]),(33,2,new byte[12]),(49,3,Text("survivor")),(123456789,4,new byte[]{1,2,3,4,5})));
var manager=Manager();manager.LoadFilesReadOnly(broken);var file=manager.assetsFileList.Single();
Check(file.ParseStatuses[1].Status=="parser-failed"&&file.ParseStatuses[2].Status=="parser-failed","malformed AnimationClip and primitive-only MeshFilter are bounded");
Check(file.ObjectsDic[3].Name=="survivor"&&file.ParseStatuses[3].Status=="typed-complete","next object survives a malformed predecessor");
Check((int)file.ObjectsDic[4].type==123456789&&file.ParseStatuses[4].Status=="generic-raw","unknown class retains numeric identity and raw status");
Check(file.ParseStatuses.Values.All(s=>s.ConsumedBytes>=0&&s.RemainingBytes>=0),"parse counters stay inside object ranges");
foreach(var obj in file.Objects){var row=file.m_Objects.Single(m=>m.m_PathID==obj.m_PathID);Check(obj.GetRawData().SequenceEqual(File.ReadAllBytes(broken).AsSpan((int)row.byteStart,(int)row.byteSize).ToArray()),"byte-exact raw export "+obj.m_PathID);}
manager.Clear();
// Small DBs follow the same lazy-preview policy as large ones: names/identities
// are visible without running a heavy parser, even if its payload is malformed.
var heavyTypes=new[]{ClassIDType.Mesh,ClassIDType.AnimationClip,ClassIDType.Material,ClassIDType.Shader,ClassIDType.Font,ClassIDType.AudioClip,ClassIDType.VideoClip,ClassIDType.MovieTexture};
var lazy=Write("lazy.assets",Serialized(heavyTypes.Select((type,index)=>((int)type,(long)index+1,Text(type.ToString()))).ToArray()));
manager=Manager();manager.DeferHeavyObjects=true;manager.LoadFilesReadOnly(lazy);file=manager.assetsFileList.Single();
Check(file.Objects.Count==heavyTypes.Length&&file.Objects.All(o=>o is DeferredObject)&&file.ParseStatuses.Values.All(s=>s.Status=="deferred"),"all eight heavy asset kinds retain identities without decoding unclicked payloads");
var font=file.Objects.Single(o=>o.type==ClassIDType.Font);try{DeferredObject.Resolve(font);}catch(EndOfStreamException){}catch(InvalidDataException){}
Check(file.ParseStatuses[font.m_PathID].Status=="parser-failed"&&file.ParseStatuses.Where(p=>p.Key!=font.m_PathID).All(p=>p.Value.Status=="deferred"),"previewing a malformed font does not decode unrelated mesh, animation, audio, or material objects");
manager.Clear();
var a=Write("a/same.assets",Serialized((49,777,Text("one"))));var b=Write("b/same.assets",Serialized((49,777,Text("two"))));
foreach(var order in new[]{new[]{a,b},new[]{b,a}}){manager=Manager();manager.LoadFilesReadOnly(order);Check(manager.assetsFileList.Count==2&&manager.assetsFileList.SelectMany(f=>f.Objects).Select(o=>o.Name).Order().SequenceEqual(new[]{"one","two"}),"same file name / PathID retained across sources");manager.Clear();}
// HOK strips type tree nodes, not the dependency arrays or managed-reference
// type names. A nonzero dependency count used to shift every following table.
byte[] ManagedReferenceFile(){
 using var meta=new MemoryStream();using var w=new BinaryWriter(meta);
 w.Write("2022.3.5f1\0"u8);w.Write(13);w.Write(true);w.Write(1);
 w.Write(49);w.Write(false);w.Write((short)-1);w.Write(new byte[16]);w.Write(2);w.Write(0);w.Write(1);
 w.Write(1);while((48+meta.Position)%4!=0)w.Write((byte)0);w.Write(888L);w.Write(0L);byte[] data=Text("managed-dependencies-survivor");w.Write((uint)data.Length);w.Write(0);
 w.Write(0);w.Write(0);w.Write(2);
 foreach(var (name,index) in new[]{("FirstNode",(short)0),("SecondNode",(short)1)}){w.Write(-1);w.Write(false);w.Write(index);w.Write(new byte[32]);w.Write(Encoding.UTF8.GetBytes(name+"\0Fixture.Graph\0Fixture.Runtime\0"));}
 w.Write("fixture-user-info\0"u8);int offset=(48+(int)meta.Length+15)/16*16;var bytes=new byte[offset+data.Length];
 BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8),22);BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20),(uint)meta.Length);BinaryPrimitives.WriteInt64BigEndian(bytes.AsSpan(24),bytes.Length);BinaryPrimitives.WriteInt64BigEndian(bytes.AsSpan(32),offset);meta.ToArray().CopyTo(bytes,48);data.CopyTo(bytes,offset);return bytes;
}
var managed=Write("managed-types.assets",ManagedReferenceFile());manager=Manager();manager.LoadFilesReadOnly(managed);file=manager.assetsFileList.Single();
Check(file.m_Types[0].m_TypeDependencies.SequenceEqual(new[]{0,1})&&file.ObjectsDic[888].Name=="managed-dependencies-survivor","nonempty HOK type dependency arrays preserve the object table");
Check(file.m_RefTypes.Select(t=>t.m_KlassName).SequenceEqual(new[]{"FirstNode","SecondNode"})&&file.m_RefTypes.All(t=>t.m_NameSpace=="Fixture.Graph"&&t.m_AsmName=="Fixture.Runtime")&&file.userInformation=="fixture-user-info","all managed-reference names and trailing metadata remain aligned");manager.Clear();
var unique=Write("c/unique.assets",Serialized((49,999,Text("unique"))));manager=Manager();manager.LoadFilesReadOnly(a,b,unique);var af=manager.assetsFileList[0];var bf=manager.assetsFileList[1];var cf=manager.assetsFileList[2];
af.originalPath=bf.originalPath="C:/fixture/package.db";cf.originalPath="C:/fixture/other.db";af.m_Externals.Add(new FileIdentifier{fileName="",pathName=""});
Check(!new PPtr<AssetStudio.Object>(1,999,af).TryGet(out _),"GUID-only references never borrow a unique PathID from another DB");
cf.originalPath=af.originalPath;var added=Write("d/added.assets",Serialized((49,1001,Text("added"))));manager.LoadFilesReadOnly(added);
Check(new PPtr<AssetStudio.Object>(1,999,af).TryGet(out var target)&&target.Name=="unique","same-source object index is refreshed after additional files load");
cf.m_Externals.Add(new FileIdentifier{fileName="",pathName=""});Check(!new PPtr<AssetStudio.Object>(1,777,cf).TryGet(out _),"ambiguous PathIDs inside one source stay unresolved");manager.Clear();
Write("many-errors.assets",Serialized(Enumerable.Range(1,105).Select(i=>(74,(long)i,new byte[4])).Append((49,1000L,Text("last"))).ToArray()));
Write("pages.assets",Serialized(Enumerable.Range(1,401).Select(i=>(49,(long)i,Text("row"+i))).ToArray()));
if(args.Length>1){
 var paths=Directory.GetFiles(Path.GetFullPath(args[1]),"*.db");var hashes=paths.ToDictionary(p=>p,p=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))));
 manager=Manager();manager.LoadFilesReadOnly(paths);var all=manager.assetsFileList.SelectMany(f=>f.Objects).ToArray();var clips=all.OfType<AnimationClip>().ToArray();
 Check(manager.assetsFileList.Count==196&&all.Length==3685,"19306: all 196 SerializedFiles / 3685 objects");
 Check(all.OfType<Texture2D>().Count()==108&&all.OfType<AnimatorController>().Count()==8&&clips.Length==12,"19306: 108 textures / 8 controllers / 12 clips");
 Check(manager.assetsFileList.SelectMany(f=>f.ParseStatuses.Values).All(s=>s.Status!="parser-failed"),"19306: no typed parser failures; partial statuses retained");
 int exact=0;
 foreach(var f in manager.assetsFileList)foreach(var obj in f.Objects){var entry=manager.ContainerEntries.Single(e=>e.Id==f.containerEntryId&&e.Source==f.originalPath);var bytes=obj.GetRawData();if(!bytes.AsSpan().SequenceEqual(entry.Data.AsSpan(checked((int)(f.containerByteOffset+obj.reader.byteStart)),checked((int)obj.byteSize))))throw new Exception("Raw bytes changed");exact++;}
 Check(exact==3685,"19306: all original object ranges byte-exact including nested files");
 Check(all.OfType<Mesh>().Count()==85&&all.OfType<Mesh>().All(m=>m.m_Colors==null||m.m_Colors.All(float.IsFinite)),"all 85 meshes have finite vertex colors");
 foreach(var clip in clips){var text=clip.Convert();Check(text==clip.Convert(),"idempotent .anim export: "+clip.Name);if(clip.m_Legacy){int curves=clip.m_RotationCurves.Count+clip.m_PositionCurves.Count+clip.m_ScaleCurves.Count+clip.m_FloatCurves.Count+clip.m_EulerCurves.Count;Check(curves==clip.m_HokLegacyAnimation.bindings.Count,"all legacy bindings exported: "+clip.Name);}}
 var model=new ModelConverter("Animation verification",new List<GameObject>(),new ModelConverter.Options{game=GameManager.GetGame(GameType.HonorOfKings)},clips.Where(c=>c.m_Legacy).ToArray());
 Check(model.AnimationList.Count==4&&model.AnimationList.All(a=>a.TrackList.Any(t=>t.Rotations.Count+t.Translations.Count+t.Scalings.Count>0)),"FBX model converter receives all four legacy animation tracks");
 Check(manager.ContainerEntries.Any(e=>e.Kind=="QtsPackageMetadata"&&e.Data.Length==116),"116-byte package metadata retained in full");
 var metadata=manager.ContainerEntries.Where(e=>e.Kind is "QtsRawMetadata" or "QtsPackageMetadata").ToArray();
 Check(metadata.Length==231&&metadata.Sum(e=>e.Data.Length)==12112,"all 231 index/package records, 12112 bytes retained");
 manager.Clear();Check(paths.All(p=>hashes[p]==Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)))) ,"input hashes unchanged");
}
Console.WriteLine(JsonSerializer.Serialize(new{passed=checks.Count,checks,scratch}));
