using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using AssetStudio;
using Hok.Worker;

internal static class ZipContainerTests
{
 internal static void Run(Action<bool,string> check,Action<Action,string> reject,string[] args)
 {
  byte[] Make(params (string Name,byte[] Bytes)[] entries){using var output=new MemoryStream();using(var zip=new ZipArchive(output,ZipArchiveMode.Create,true))foreach(var item in entries){using var member=zip.CreateEntry(item.Name).Open();member.Write(item.Bytes);}return output.ToArray();}
  var bytes=Make(("folder/",[]),("../same.txt","first"u8.ToArray()),("../same.txt","second"u8.ToArray()),("image.gif","GIF89a"u8.ToArray()));
  var index=ZipContainerData.ReadIndex(()=>new MemoryStream(bytes));
  check(index.Kind=="ZipArchive"&&!index.IsNumpy&&index.Members.Length==4&&index.Members[0].Directory,"ordinary ZIP is not interpreted as NPZ; directory and duplicate records retained");
  byte[] Read(ZipContainerData.Member m){using var input=ZipContainerData.OpenMember(()=>new MemoryStream(bytes),m);using var output=new MemoryStream();input.CopyTo(output);return output.ToArray();}
  check(Encoding.UTF8.GetString(Read(index.Members[1]))=="first"&&Encoding.UTF8.GetString(Read(index.Members[2]))=="second","duplicate/traversal-like member names remain distinct indexed streams without filesystem extraction");
  reject(()=>ZipContainerData.OpenMember(()=>new MemoryStream(bytes),index.Members[1] with {Name="changed"}),"ZIP lazy member identity changes rejected");
  int opens=0;var backing=new ContainerEntry("zip/1","fixture",()=>{opens++;return ZipContainerData.OpenMember(()=>new MemoryStream(bytes),index.Members[1]);},5,"ArchiveMember","../same.txt",index.Members[1].Head);
  check(Encoding.UTF8.GetString(backing.Head())=="first"&&opens==0,"archive classification uses cached bounded prefixes rather than materializing/reopening every member");
  using(var stream=backing.Open()){check(stream.Length==5&&stream.ReadByte()=='f',"archive payload opens only on demand");}
  var corrupt=(byte[])bytes.Clone();int central=corrupt.AsSpan().IndexOf("PK\x01\x02"u8);central=corrupt.AsSpan(central+4).IndexOf("PK\x01\x02"u8)+central+4;
  BinaryPrimitives.WriteUInt32LittleEndian(corrupt.AsSpan(central+16),123);
  var badIndex=ZipContainerData.ReadIndex(()=>new MemoryStream(corrupt));
  reject(()=>{using var input=ZipContainerData.OpenMember(()=>new MemoryStream(corrupt),badIndex.Members[1]);input.CopyTo(Stream.Null);},"ZIP native export verifies full uncompressed CRC");
  reject(()=>ZipContainerData.ReadIndex(()=>new MemoryStream(bytes[..^10])),"truncated ZIP central directory rejected");
  var png=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/l9sAAAAASUVORK5CYII=");
  var image=new ResourceAsset("png","image.png","fixture","ImageFile","png",png);
  check(image.Preview=="image"&&image.Formats.Contains("png")&&image.Formats.Contains("original"),"native image files advertise usable PNG/native exports");
  check(ResourceAsset.Detect("RIFF\0\0\0\0WEBP"u8.ToArray(),"ArchiveMember",12)==("ImageFile","webp")&&ResourceAsset.Detect("GIF89a"u8.ToArray(),"ArchiveMember")==("ImageFile","gif"),"WebP and GIF archive members are image assets");
  if(args.Length==0)return;
  string apk=Path.Combine(args[0],"probes/zip-17997877637375806120/17997877637375806120.payload");if(!File.Exists(apk))return;
  var real=ZipContainerData.ReadIndex(()=>File.OpenRead(apk));
  check(real.Kind=="AndroidPackage"&&real.Members.Length==3139&&real.Members.All(m=>m.Error==null),"real APK directory enumerates all 3139 members without NPZ misclassification");
  foreach(var member in real.Members.Where(m=>!m.Directory)){using var input=ZipContainerData.OpenMember(()=>File.OpenRead(apk),member);input.CopyTo(Stream.Null);}
  check(true,"all real APK member streams pass full length/CRC validation");
 }
}
