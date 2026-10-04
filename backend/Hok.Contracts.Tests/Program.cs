using System.Text.Json;
using Hok.Contracts;
var projectRoot=Path.GetFullPath(args[0]);
using var document=JsonDocument.Parse(File.ReadAllText(Path.Combine(projectRoot,"planning/identity-test-vectors.json")));
int count=0;
foreach(var vector in document.RootElement.GetProperty("cases").EnumerateArray()){
 var name=vector.GetProperty("filename").GetString()!;var result=Identity.Parse(name);var valid=vector.GetProperty("valid").GetBoolean();
 if((result is not null)!=valid)throw new Exception("Validity mismatch: "+name);
 if(result is not null&&(result.HeroId!=vector.GetProperty("heroId").GetString()||result.SkinId!=vector.GetProperty("skinId").GetString()||result.Shard!=vector.GetProperty("shard").GetString()))throw new Exception("Identity mismatch: "+name);
 count++;
}
if(Identity.Parse("3200010704",true)?.SkinId!="10704")throw new Exception("Verified extensionless DB identity");
if(Identity.Placeholder("10704")!=Identity.Placeholder("10704"))throw new Exception("Unstable fallback");
var temp=Directory.CreateTempSubdirectory("hok-contracts-").FullName;
var unknown=Path.Combine(temp,"3200099904.db");File.WriteAllBytes(unknown,[1,2,3]);
var unsigned=Path.Combine(temp,"3200099905");File.WriteAllBytes(unsigned,[1,2,3]);
var signed=Path.Combine(temp,"3200099906");File.WriteAllBytes(signed,[1,0,0,2,1,2,3,4]);
var stamp=File.GetLastWriteTimeUtc(unknown);
var scan=Scanner.Scan([temp,unknown],CancellationToken.None);
if(scan.Files.Count!=2||scan.Files.Any(f=>f.HeroId!="999"))throw new Exception("Scanner unknown ID / signature / dedup contract");
if(File.GetLastWriteTimeUtc(unknown)!=stamp)throw new Exception("Scanner changed input");
// Folder imports must visit every ordinary descendant, not just named package directories.
var recursiveRoot=Path.Combine(temp,"recursive");
var nested=recursiveRoot;
for(var level=1;level<=12;level++){
 nested=Path.Combine(nested,"子目录 "+level);Directory.CreateDirectory(nested);
 File.WriteAllBytes(Path.Combine(nested,$"32000105{level:00}.db"),[1,2,3]);
}
var shard=Path.Combine(nested,"3200010512_0.db");File.WriteAllBytes(shard,[1,2,3]);
var extensionless=Path.Combine(nested,"3200099906");File.WriteAllBytes(extensionless,[1,0,0,2,1,2,3,4]);
File.WriteAllText(Path.Combine(nested,"notes.txt"),"Not a DB");
Directory.CreateDirectory(Path.Combine(recursiveRoot,"empty"));
var nestedScan=Scanner.Scan([recursiveRoot],CancellationToken.None);
if(nestedScan.Files.Count!=14||nestedScan.Errors.Count!=0||nestedScan.Visited!=15)throw new Exception("Recursive folder scan missed nested files");
if(nestedScan.Files.Count(f=>f.HeroId=="105")!=13||nestedScan.Files.Single(f=>f.Path==shard).Shard!="0"||nestedScan.Files.Single(f=>f.Path==extensionless).HeroId!="999")throw new Exception("Recursive identity / shard recognition mismatch");
if(nestedScan.Files.Any(f=>f.Root!=recursiveRoot))throw new Exception("Nested files must retain the selected workspace root");
var overlap=Scanner.Scan([recursiveRoot,nested,shard],CancellationToken.None);
if(overlap.Files.Count!=14||overlap.Files.Select(f=>f.Path).Distinct().Count()!=14)throw new Exception("Overlapping folder imports duplicated DBs");
using var cancelled=new CancellationTokenSource();cancelled.Cancel();
try{Scanner.Scan([recursiveRoot],cancelled.Token);throw new Exception("Cancelled scan continued");}catch(OperationCanceledException){}
// Arbitrary DB names still enter the exact same parser/preview/export pipeline.
var arbitraryRoot=Path.Combine(temp,"arbitrary");Directory.CreateDirectory(arbitraryRoot);
foreach(var folder in new[]{"a","b"}){
 var directory=Path.Combine(arbitraryRoot,folder);Directory.CreateDirectory(directory);
 foreach(var filename in new[]{"0.db","0_0.db","中文资源.db","中文资源_12.DB"})File.WriteAllBytes(Path.Combine(directory,filename),[1,2,3]);
 File.WriteAllText(Path.Combine(directory,"record.bytes"),"not a DB");
 File.WriteAllBytes(Path.Combine(directory,"sound"),[1,0,0,2,1,2,3,4]);
 File.WriteAllText(Path.Combine(directory,"notes"),"not a recognized container");
}
var arbitrary=Scanner.Scan([arbitraryRoot],CancellationToken.None);
if(arbitrary.Files.Count!=10||arbitrary.Files.Any(f=>f.HeroId!=""||!f.SkinId.StartsWith("unknown:")))throw new Exception("Arbitrary DB files excluded or misclassified");
if(arbitrary.Files.Select(f=>f.SkinId).Distinct().Count()!=6)throw new Exception("Unknown packages from different directories merged");
var zero=arbitrary.Files.First(f=>f.Name=="0.db");
var zeroGroup=Scanner.SelectPackageFiles(arbitrary.Files,zero.SkinId,zero.Id);
if(zeroGroup.Length!=2||zeroGroup[0].Name!="0.db"||zeroGroup[1].Name!="0_0.db")throw new Exception("Unknown package shard association");
var repeated=Scanner.Scan([arbitraryRoot,zero.Path],CancellationToken.None);
if(repeated.Files.Count!=10||!repeated.Files.Select(f=>f.SkinId).SequenceEqual(arbitrary.Files.Select(f=>f.SkinId)))throw new Exception("Unstable unknown identity or duplicate import");
if(Identity.ParsePackage("texture_pack_12.db") is not {Stem:"texture_pack",Shard:"12",HeroId:""})throw new Exception("Arbitrary name suffix parsing");
if(Identity.ParsePackage("notes.txt") is not null||Identity.ParsePackage("0") is not null)throw new Exception("Unsigned unrelated files accepted");
if(Scanner.SelectPackageFiles(arbitrary.Files,zero.SkinId,zeroGroup[1].Id).Length!=2)throw new Exception("Selecting shard must load its complete group");
Console.WriteLine("PASS 7 arbitrary-name/grouping regressions; unknown DB names retain full asset functionality");
Console.WriteLine(JsonSerializer.Serialize(new{passed=count+9,filenameVectors=count,extensionlessSignature=true,stableFallback=true,scannerUnknownAndDedup=true,readOnly=true,recursiveLevels=12,recursiveFiles=nestedScan.Files.Count,recursiveIdentityAndShards=true,workspaceRootPreserved=true,overlappingFoldersDeduplicated=true,cancellation=true,fixtureDirectory=temp}));
