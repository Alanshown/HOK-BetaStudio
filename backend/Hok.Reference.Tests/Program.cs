using AssetStudio;
using System.Buffers.Binary;
using System.Reflection;
using System.Text;
using System.Text.Json;

Logger.Silent=true;
var root=Path.GetFullPath(args[0]);var scratch=Path.Combine(root,".cache","reference-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(scratch);
var results=new List<object>();int failed=0;
void Check(bool success,string name){results.Add(new{name,success});Console.WriteLine((success?"PASS ":"FAIL ")+name);if(!success)failed++;}
byte[] Serialized(string name,string? external=null){
 using var data=new MemoryStream();using var dw=new BinaryWriter(data);dw.Write(Encoding.UTF8.GetByteCount(name));dw.Write(Encoding.UTF8.GetBytes(name));while(data.Position%4!=0)dw.Write((byte)0);dw.Write(4);dw.Write(new byte[]{1,2,3,4});
 using var meta=new MemoryStream();using var w=new BinaryWriter(meta);w.Write("2018.4.0f1\0"u8);w.Write(13);w.Write(false);w.Write(1);w.Write(49);w.Write(false);w.Write((short)-1);w.Write(new byte[16]);w.Write(0);w.Write(1);while((20+meta.Position)%4!=0)w.Write((byte)0);w.Write(777L);w.Write(0);w.Write((uint)data.Length);w.Write(0);w.Write(0);w.Write(external is null?0:1);if(external is not null){w.Write((byte)0);w.Write(new byte[16]);w.Write(0);w.Write(Encoding.UTF8.GetBytes(external));w.Write((byte)0);}w.Write((byte)0);
 int offset=(20+(int)meta.Length+15)/16*16;var bytes=new byte[offset+data.Length];BinaryPrimitives.WriteInt32BigEndian(bytes,(int)meta.Length);BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(4),bytes.Length);BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(8),17);BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(12),offset);meta.ToArray().CopyTo(bytes,20);data.ToArray().CopyTo(bytes,offset);return bytes;
}
string Put(string name,byte[] bytes){var p=Path.GetFullPath(Path.Combine(scratch,name));Directory.CreateDirectory(Path.GetDirectoryName(p)!);File.WriteAllBytes(p,bytes);return p;}
AssetsManager Manager()=>new(){Game=GameManager.GetGame(GameType.HonorOfKings)};
var owners=new[]{Put("甲/owner.assets",Serialized("owner-a","shared.assets")),Put("乙/owner.assets",Serialized("owner-b","shared.assets"))};
var shared=new[]{Put("甲/shared.assets",Serialized("one")),Put("乙/shared.assets",Serialized("two"))};
var media=new[]{Put("甲/shared.resS",[1,2,3,4]),Put("乙/shared.resS",[9,8,7,6])};
foreach(var order in new[]{new[]{0,1},new[]{1,0}}){
 var manager=Manager();manager.LoadFilesReadOnly(order.Select(i=>owners[i]).ToArray());
 Check(manager.assetsFileList.Count==4,"auto-load distinct same-name dependencies: "+string.Join(',',order));
 manager.Clear();manager=Manager();manager.LoadFilesReadOnly(order.Select(i=>owners[i]).Concat(order.Select(i=>shared[i])).ToArray());
 foreach(var i in order){
  var owner=manager.assetsFileList.Single(f=>f.fullName==owners[i]);var ptr=new PPtr<TextAsset>(1,777,owner);
  Check(ptr.TryGet(out var target)&&target.Name==(i==0?"one":"two"),"PPtr resolves local directory "+i+" / "+string.Join(',',order));
  var resource=new ResourceReader("shared.resS",owner,0,4);
  Check(resource.GetData().SequenceEqual(File.ReadAllBytes(media[i])),"same-name resS bytes isolated "+i+" / "+string.Join(',',order));
  var exported=Path.Combine(scratch,$"export-{order[0]}-{i}.bin");resource.WriteData(exported);
  Check(File.ReadAllBytes(exported).SequenceEqual(File.ReadAllBytes(media[i])),"resS export bytes isolated "+i+" / "+string.Join(',',order));
 }
 manager.Clear();
}
// Missing local data must not borrow another directory's otherwise unique alias.
var missing=Put("missing/owner.assets",Serialized("missing","shared.assets"));
{
 var m=Manager();m.LoadFilesReadOnly(missing,owners[0],shared[0]);var local=m.assetsFileList.Single(f=>f.fullName==owners[0]);var absent=m.assetsFileList.Single(f=>f.fullName==missing);
 _=new ResourceReader("shared.resS",local,0,4).GetData();
 Check(!new PPtr<TextAsset>(1,777,absent).TryGet(out _),"missing local reference does not bind foreign unique file");
 bool rejected=false;try{_=new ResourceReader("shared.resS",absent,0,4).GetData();}catch(FileNotFoundException){rejected=true;}
 Check(rejected,"missing local resS does not bind foreign warm cache");m.Clear();
}
// Ambiguous recursive descendants must not depend on enumeration order.
var ambiguous=Put("ambiguous/owner.assets",Serialized("ambiguous","shared.assets"));
Put("ambiguous/a/shared.assets",Serialized("left"));Put("ambiguous/b/shared.assets",Serialized("right"));Put("ambiguous/a/shared.resS",[1,2,3,4]);Put("ambiguous/b/shared.resS",[5,6,7,8]);
{
 var m=Manager();m.LoadFilesReadOnly(ambiguous);Check(m.assetsFileList.Count==1,"ambiguous recursive dependency is not guessed");bool rejected=false;try{_=new ResourceReader("shared.resS",m.assetsFileList[0],0,4).GetData();}catch(IOException){rejected=true;}Check(rejected,"ambiguous recursive resS is not guessed");m.Clear();
}
// Explicit subpaths take priority and a missing explicit subpath must not
// silently resolve another same-name file somewhere under the package root.
var explicitOwner=Put("ambiguous/explicit.assets",Serialized("explicit","b/shared.assets"));
{
 var m=Manager();m.LoadFilesReadOnly(explicitOwner);var owner=m.assetsFileList.Single(f=>f.fullName==explicitOwner);
 Check(new PPtr<TextAsset>(1,777,owner).TryGet(out var target)&&target.Name=="right","explicit relative external path wins");
 Check(new ResourceReader("a/shared.resS",owner,0,4).GetData().SequenceEqual(new byte[]{1,2,3,4}),"explicit relative resS path wins");
 bool rejected=false;try{_=new ResourceReader("absent/shared.resS",owner,0,4).GetData();}catch(FileNotFoundException){rejected=true;}Check(rejected,"missing explicit subpath cannot use a different descendant");m.Clear();
}
foreach(var order in new[]{new[]{0,1},new[]{1,0}}){
 var m=Manager();m.LoadFilesReadOnly(owners.Concat(shared).ToArray());
 var register=typeof(AssetsManager).GetMethod("RegisterResource",BindingFlags.NonPublic|BindingFlags.Instance)!;
 foreach(var i in order){
  var owner=m.assetsFileList.Single(f=>f.fullName==owners[i]);var target=m.assetsFileList.Single(f=>f.fullName==shared[i]);
  owner.originalPath=target.originalPath=Path.Combine(scratch,$"bundle-{i}.db");
  register.Invoke(m,["shared.resS",new BinaryReader(new MemoryStream(File.ReadAllBytes(media[i]))),owner.originalPath]);
 }
 foreach(var i in order){
  var owner=m.assetsFileList.Single(f=>f.fullName==owners[i]);
  Check(new PPtr<TextAsset>(1,777,owner).TryGet(out var target)&&target.Name==(i==0?"one":"two"),"container-scoped external identity "+i+" / "+string.Join(',',order));
  Check(new ResourceReader("shared.resS",owner,0,4).GetData().SequenceEqual(File.ReadAllBytes(media[i])),"container-scoped memory resource "+i+" / "+string.Join(',',order));
 }
 Check(m.ResourceFiles.Count()==2&&m.ResourceFiles.Select(x=>x.Key).Distinct().Count()==2,"raw resource enumeration preserves both source identities");
 var first=m.assetsFileList.Single(f=>f.fullName==owners[0]);var second=m.assetsFileList.Single(f=>f.fullName==owners[1]);var ptr=new PPtr<TextAsset>(0,777,first);ptr.Set((TextAsset)second.ObjectsDic[777]);
 Check(ptr.m_FileID!=0&&ptr.TryGet(out var setTarget)&&ReferenceEquals(setTarget,second.ObjectsDic[777]),"Set distinguishes equal basenames and preserves exact target");
 m.Clear();
}
// A missing target cached before an incremental load must become resolvable.
{
 var lateOwner=Put("late/owner.assets",Serialized("late","shared.assets"));var m=Manager();m.LoadFilesReadOnly(lateOwner);var ptr=new PPtr<TextAsset>(1,777,m.assetsFileList[0]);Check(!ptr.TryGet(out _),"missing reference initially unresolved");
 var late=Put("late/shared.assets",Serialized("late-target"));m.LoadFilesReadOnly(late);Check(ptr.TryGet(out var target)&&target.Name=="late-target","reference cache invalidates when files are added");m.Clear();
}
// GUID-only HOK slots: prefer local identity, otherwise require a unique
// object inside the exact same DB. Unrelated sources never qualify.
{
 var ownerPath=Put("guid/owner.assets",Serialized("guid-owner",""));
 var targetPath=Put("guid/target.assets",Serialized("guid-target"));
 var foreignPath=Put("foreign/target.assets",Serialized("foreign-target"));
 var m=Manager();m.LoadFilesReadOnly(ownerPath,targetPath,foreignPath);
 var owner=m.assetsFileList.Single(f=>f.fullName==ownerPath);
 var target=m.assetsFileList.Single(f=>f.fullName==targetPath);
 var foreign=m.assetsFileList.Single(f=>f.fullName==foreignPath);
 owner.originalPath=target.originalPath=Path.Combine(scratch,"local.db");
 foreign.originalPath=Path.Combine(scratch,"foreign.db");
 var pointer=new PPtr<TextAsset>(1,777,owner);
 Check(pointer.TryGet(out var local)&&local.Name=="guid-owner","GUID-only local object takes priority");
 owner.ObjectsDic.Remove(777);
 Check(pointer.TryGet(out var scoped)&&scoped.Name=="guid-target","GUID-only reference resolves unique same-DB target");
 target.originalPath=foreign.originalPath;
 Check(!pointer.TryGet(out _),"GUID-only missing target cannot borrow foreign DB objects");
 target.originalPath=foreign.originalPath=owner.originalPath;
 Check(!pointer.TryGet(out _),"ambiguous GUID-only same-DB objects are rejected");
 foreign.originalPath=Path.Combine(scratch,"foreign.db");
 Check(pointer.TryGet(out var unique)&&unique.Name=="guid-target","GUID-only target re-resolves without stale alias cache");
 m.Clear();
}
// Reusing an output name must not leave stale bytes from a longer export.
{
 using var data=new BinaryReader(new MemoryStream(new byte[]{2,4,6}));
 var output=Put("truncate.bin",new byte[100]);
 new ResourceReader(data,0,3).WriteData(output);
 Check(File.ReadAllBytes(output).SequenceEqual(new byte[]{2,4,6}),"resource export truncates an existing longer output");
}
Console.WriteLine(JsonSerializer.Serialize(new{passed=results.Count-failed,failed,results,scratch}));Environment.ExitCode=failed==0?0:1;
