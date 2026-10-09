using AssetStudio;
using System.Text.Json;
using System.Security.Cryptography;
if(args.Length>2&&args[2]=="--recover"){Recovery.Run(args[0],args[1]);return;}
var paths=File.ReadAllLines(args[0]).Where(x=>!string.IsNullOrWhiteSpace(x)&&!x.StartsWith('#'));
var results=new List<object>();
foreach(var dir in paths)foreach(var path in Directory.GetFiles(dir,"*.db").OrderBy(x=>x)){
 using var stream=File.OpenRead(path);string sha=Convert.ToHexString(SHA256.HashData(stream));
 using var reader=new FileReader(path);reader.Endian=EndianType.LittleEndian;
 object header=null;string error=null;object[] entries=[];
 try{var h=new QtsVFSFile.FHoKHeader(reader);header=h;var q=new QtsVFSFile(reader);entries=q.Entries.Select(e=>(object)new{fileId=e.Key.ToString(),chunks=e.Value.Select(c=>new{c.Offset,c.CompressedSize,c.UncompressedSize,c.MainBlock,c.SubBlock}).ToArray()}).ToArray();}catch(Exception e){error=e.ToString();}
 results.Add(new{path,size=new FileInfo(path).Length,sha256=sha,header,entries,error});
 Console.Error.WriteLine(path+": "+entries.Length);
}
File.WriteAllText(args[1],JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true,IncludeFields=true,PropertyNamingPolicy=JsonNamingPolicy.CamelCase}));
