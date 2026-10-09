using Hok.Desktop;
var parent=Path.GetFullPath(args[0]);Directory.CreateDirectory(parent);
var owned=Path.Combine(parent,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(owned);
var file=Path.Combine(owned,"worker.cache");File.WriteAllBytes(file,[1,2,3]);
var handle=File.Open(file,FileMode.Open,FileAccess.Read,FileShare.None);
var release=Task.Run(async()=>{await Task.Delay(200);handle.Dispose();});
long cleaned=OwnedCache.Clear(owned,parent);await release;
if(cleaned!=3||Directory.EnumerateFileSystemEntries(owned).Any())throw new Exception("Transient file lock was not cleaned");
Console.WriteLine("PASS cleanup waits for transient worker/cache file locks");
try{OwnedCache.Clear(parent,parent);throw new Exception("Unsafe path accepted");}catch(IOException){Console.WriteLine("PASS cache ownership checks remain enforced");}
File.WriteAllBytes(file,[1]);using(var permanent=File.Open(file,FileMode.Open,FileAccess.Read,FileShare.None)){
try{OwnedCache.Clear(owned,parent);throw new Exception("Permanent lock silently ignored");}catch(IOException){Console.WriteLine("PASS persistent lock is still reported as a failure");}}
OwnedCache.Clear(owned,parent);
