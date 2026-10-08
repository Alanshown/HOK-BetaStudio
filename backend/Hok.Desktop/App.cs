using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;
using Hok.Contracts;
using Hok.Catalog;
namespace Hok.Desktop;
internal sealed class App:Application {
 internal static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
 [STAThread] static void Main(string[] args){var app=new App();app.Run(new MainWindow(args));}
}
internal sealed class MainWindow:Window {
 readonly WebView2 browser=new();readonly string root=AppContext.BaseDirectory;
 readonly string data=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"HokBetaStudio");
 readonly CatalogService catalog;
 readonly string cache,previewCache,exportCache;readonly WorkerClient worker,previewWorker,exportWorker; readonly SemaphoreSlim requests=new(1,1),previewLane=new(1,1),exportLane=new(1,1);
 Task previewCleanup=Task.CompletedTask;
 bool sharedHeavyLane;CancellationTokenSource previewCancellation=new(),exportCancellation=new();
 DbRecord[] activeFiles=[];int previewGeneration=-1,previewEpoch,previewRequest,exportEpoch;bool closing,resetting;
 readonly string[] args; List<DbRecord> files=[];int generation;CancellationTokenSource? scanCancel;
 public MainWindow(string[] startup){
  args=startup;
  if(args.Contains("--smoke")){var isolated=args.FirstOrDefault(a=>a.StartsWith("--smoke-data=",StringComparison.Ordinal));if(isolated is not null)data=Path.GetFullPath(isolated[13..]);}
  Directory.CreateDirectory(data);catalog=new(Path.Combine(data,"catalog"),Path.Combine(root,"assets/catalog/remote-index.seed.json"),Path.Combine(root,"assets/catalog/corrections.default.json"));cache=Path.Combine(data,"cache",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(cache);
  previewCache=Path.Combine(cache,Guid.NewGuid().ToString("N"));exportCache=Path.Combine(cache,Guid.NewGuid().ToString("N"));
  worker=new(root,cache,Path.Combine(data,"worker.log"));previewWorker=new(root,previewCache,Path.Combine(data,"preview.log"));exportWorker=new(root,exportCache,Path.Combine(data,"export.log"));Title="HOK BetaStudio";Width=1440;Height=920;MinWidth=1000;MinHeight=700;WindowStartupLocation=WindowStartupLocation.CenterScreen;
  var icon=Path.Combine(root,"assets/icons/app/app-icon-hok-256.png");if(File.Exists(icon))Icon=new BitmapImage(new Uri(icon));
  if(args.Contains("--smoke")){WindowState=WindowState.Minimized;ShowInTaskbar=false;}
  Content=browser;Loaded+=async(_,_)=>await Init();Closed+=(_,_)=>{closing=true;catalog.Dispose();scanCancel?.Cancel();worker.Dispose();previewWorker.Dispose();exportWorker.Dispose();browser.Dispose();};
 }
 async Task Init(){
  try{
   var options=args.Contains("--smoke")?new CoreWebView2EnvironmentOptions("--remote-debugging-port=9224"):null;
   var env=await CoreWebView2Environment.CreateAsync(null,Path.Combine(data,args.Contains("--smoke")?"WebView2-Smoke":"WebView2"),options);await browser.EnsureCoreWebView2Async(env);
   var core=browser.CoreWebView2;core.Settings.AreDefaultContextMenusEnabled=false;core.Settings.AreDevToolsEnabled=args.Contains("--devtools");core.Settings.IsStatusBarEnabled=false;core.Settings.AreHostObjectsAllowed=false;
   core.SetVirtualHostNameToFolderMapping("hok.local",Path.Combine(root,"ui"),CoreWebView2HostResourceAccessKind.DenyCors);
   core.SetVirtualHostNameToFolderMapping("art.hok.local",Path.Combine(root,"assets"),CoreWebView2HostResourceAccessKind.Allow);
   core.SetVirtualHostNameToFolderMapping("preview.hok.local",cache,CoreWebView2HostResourceAccessKind.Allow);
   core.NavigationStarting+=(_,e)=>{if(!e.Uri.StartsWith("https://hok.local/",StringComparison.Ordinal))e.Cancel=true;};
   core.NewWindowRequested+=(_,e)=>e.Handled=true;
   core.PermissionRequested+=(_,e)=>e.State=CoreWebView2PermissionState.Deny;
   core.DownloadStarting+=(_,e)=>e.Cancel=true;
   core.WebMessageReceived+=OnMessage;core.Navigate("https://hok.local/index.html");
  }catch(Exception e){Content=new System.Windows.Controls.TextBox{Text="HOK BetaStudio\n\n"+e.Message+"\n\nMicrosoft Edge WebView2 Runtime is required.\nhttps://developer.microsoft.com/microsoft-edge/webview2/",IsReadOnly=true,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(32)};}
 }
 async void OnMessage(object? sender,CoreWebView2WebMessageReceivedEventArgs e){
  string id="";try{if(!e.Source.StartsWith("https://hok.local/",StringComparison.Ordinal)||e.WebMessageAsJson.Length>1_000_000)return;
   using var doc=JsonDocument.Parse(e.WebMessageAsJson);var req=doc.RootElement;id=req.GetProperty("id").GetString()!;var method=req.GetProperty("method").GetString()!;var p=req.GetProperty("payload").Clone();
   if(method=="catalogCheck"){Reply(id,await catalog.CheckAsync());return;}
   if(method is "clear" or "catalogApply"){
    if(resetting)throw new InvalidOperationException("Workspace cleanup is already running.");
    resetting=true;InvalidateWorkspace();
    await requests.WaitAsync();
    try{var removed=await ReleaseWorkspace();if(method=="catalogApply")Reply(id,CatalogView(await catalog.ApplyAsync(p.GetProperty("revision").GetString()!)));else Reply(id,new{cleared=true,bytes=removed,generation});}
    finally{requests.Release();resetting=false;}
    return;
   }
   if(resetting&&method!="boot")throw new InvalidOperationException("Workspace cleanup is running.");
   if(method=="cancel"){
    var lane=p.TryGetProperty("lane",out var value)?value.GetString():"all";
    if(lane is not ("parse" or "preview" or "export" or "all"))throw new InvalidOperationException("Invalid worker lane");
    if(lane is "export" or "all"){exportEpoch++;RenewCancellation(ref exportCancellation);exportWorker.Cancel();}
    if(lane is "parse" or "preview" or "all"){previewEpoch++;RenewCancellation(ref previewCancellation);previewWorker.Cancel();previewGeneration=-1;}
    if(lane is "parse" or "all"){scanCancel?.Cancel();worker.Cancel();activeFiles=[];generation++;}
    if(lane=="preview"){previewCleanup=ReleasePreview();await previewCleanup;}
    Reply(id,new{cancelled=true});return;
   }
   if(method is "preview" or "dump" or "bank" or "export"){Reply(id,await Dispatch(method,p));return;}
   if(!await requests.WaitAsync(0))throw new InvalidOperationException("An operation is already running");
   try{
    object? result;
    if(method=="drop"){
     var objects=e.AdditionalObjects;if(objects.Count==0||objects.Count>256)throw new IOException("Drop between 1 and 256 files or folders.");
     var paths=objects.OfType<CoreWebView2File>().Select(f=>f.Path).Where(path=>!string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
     if(objects.Any(obj=>obj is not CoreWebView2File)||paths.Length==0||paths.Any(path=>!Path.IsPathFullyQualified(path)||(!File.Exists(path)&&!Directory.Exists(path))))throw new IOException("Dropped file or folder is no longer available.");
     result=await OpenPaths(paths);
    }else result=await Dispatch(method,p);
    Reply(id,result);
   }finally{requests.Release();}
  }catch(Exception ex){if(!closing&&browser.CoreWebView2 is not null)browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new{id,ok=false,error=ex.GetBaseException().Message},App.Json));}
 }
 void Reply(string id,object? result){if(!closing)browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new{id,ok=true,data=result},App.Json));}
 void InvalidateWorkspace(){
  scanCancel?.Cancel();generation++;previewEpoch++;exportEpoch++;
  RenewCancellation(ref previewCancellation);RenewCancellation(ref exportCancellation);
  worker.Cancel();previewWorker.Cancel();exportWorker.Cancel();
  activeFiles=[];files=[];previewGeneration=-1;
 }
 static void RenewCancellation(ref CancellationTokenSource value){var old=value;value=new();old.Cancel();old.Dispose();}
 async Task ReleasePreview(){
  await previewLane.WaitAsync();try{if(sharedHeavyLane&&activeFiles.Length>0&&!closing)await worker.Call("releasePreview",new{});await previewWorker.StopAsync();await Task.Run(()=>OwnedCache.Clear(previewCache,cache));}
  finally{previewLane.Release();}
 }
 async Task<long> ReleaseWorkspace(){
  await Task.WhenAll(worker.StopAsync(),previewWorker.StopAsync(),exportWorker.StopAsync());
  await previewLane.WaitAsync();await exportLane.WaitAsync();
  try{var bytes=await Task.Run(()=>OwnedCache.Clear(cache,Path.Combine(data,"cache"))+OwnedCache.Clear(Path.Combine(data,"cache","editing",Path.GetFileName(cache)),Path.Combine(data,"cache","editing")));await browser.CoreWebView2.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.DiskCache);return bytes;}
  finally{exportLane.Release();previewLane.Release();}
 }
 async Task<object> OpenPaths(string[] paths){
  resetting=true;InvalidateWorkspace();
  try{await ReleaseWorkspace();}finally{resetting=false;}
  scanCancel?.Dispose();scanCancel=new();int version=generation;
  var result=await Task.Run(()=>Scanner.Scan(paths,scanCancel.Token));
  if(version!=generation)throw new OperationCanceledException();
  files=result.Files;return new{generation,files,errors=result.Errors,visited=result.Visited};
 }
 static void VerifySnapshot(DbRecord[] snapshot){foreach(var f in snapshot){var info=new FileInfo(f.Path);if(!info.Exists||$"{info.Length}:{info.LastWriteTimeUtc.Ticks}"!=f.Fingerprint)throw new IOException("Source file changed; reload the workspace.");}}
 object CatalogView(CatalogIndex index)=>new{version="1.3",revision=index.Revision,heroes=index.Records.Where(e=>e.Kind=="hero").Select(e=>new{id=e.Id,name=e.Name,portrait=e.Status=="quarantined"?"":e.ImageUrl}),skins=index.Records.Where(e=>e.Kind=="skin").Select(e=>new{skinId=e.Id,heroId=e.HeroId,name=e.Name,portrait=e.Status=="quarantined"?"":e.ImageUrl,artAvailable=e.Status!="quarantined"}),artOrigin="https://art.hok.local/",initialPaths=args.Where(a=>!a.StartsWith("--")).ToArray()};
 async Task<object?> Dispatch(string method,JsonElement p){
  switch(method){
   case "boot":
    return CatalogView(await Task.Run(()=>catalog.LoadAsync()));
   case "open":{
    var mode=p.GetProperty("mode").GetString();string[] paths;
    if(mode=="startup")paths=args.Where(a=>!a.StartsWith("--")&&(File.Exists(a)||Directory.Exists(a))).ToArray();
    else if(mode=="folder"){var d=new OpenFolderDialog{Multiselect=false};paths=d.ShowDialog(this)==true?[d.FolderName]:[];}
    else{var d=new OpenFileDialog{Multiselect=true,Filter="DB (*.db)|*.db|All files (*.*)|*.*"};paths=d.ShowDialog(this)==true?d.FileNames:[];}
    return paths.Length==0?null:await OpenPaths(paths);
   }
   case "load":{
    string skinId=p.GetProperty("skinId").GetString()!;string? dbId=p.TryGetProperty("dbId",out var d)?d.GetString():null;
    var selected=Scanner.SelectPackageFiles(files,skinId,dbId);
    foreach(var f in selected){var fi=new FileInfo(f.Path);if(!fi.Exists||$"{fi.Length}:{fi.LastWriteTimeUtc.Ticks}"!=f.Fingerprint)throw new IOException("Source file changed; open the workspace again.");}
    previewEpoch++;RenewCancellation(ref previewCancellation);previewWorker.Cancel();previewGeneration=-1;activeFiles=[];var version=++generation;
    var result=await worker.Call("load",new{paths=selected.Select(f=>f.Path).ToArray(),dependencyPaths=files.Select(f=>f.Path).ToArray()});if(version!=generation)throw new OperationCanceledException();activeFiles=selected;
    // Reusing the indexed worker avoids two or three multi-GiB copies for
    // large DBs. Small DBs keep independent preview/export workers.
    sharedHeavyLane=result.GetProperty("count").GetInt32()>100000||result.GetProperty("retainedManagedBytes").GetInt64()>512L*1024*1024;
    return new{generation,result};
   }
   case "list":return await worker.Call("list",p);
   case "rawList":case "rawDetail":return await worker.Call(method,p);
   case "rawExport":{
    VerifySnapshot(activeFiles);var output=PickExportFolder();if(output is null)return null;
    return await worker.Call("rawExport",new{ids=p.GetProperty("ids"),output});
   }
   case "replacementState":return await worker.Call("replacementState",p);
   case "replace":{
    if(activeFiles.Length==0)throw new InvalidOperationException("No active package");
    VerifySnapshot(activeFiles);
    // Hidden smoke runs can provide picker results through process arguments;
    // normal WebView messages cannot supply arbitrary filesystem paths.
    var testPath=args.Contains("--smoke")?args.FirstOrDefault(a=>a.StartsWith("--smoke-replacement=",StringComparison.Ordinal))?[20..]:null;
    if(testPath is not null)return await worker.Call("replace",new{assetId=p.GetProperty("assetId").GetString(),path=Path.GetFullPath(testPath)});
    var dialog=new OpenFileDialog{Multiselect=false,Title="Beta — original binary replacement / 原始二进制替换",Filter="Original binary (*.*)|*.*"};
    if(dialog.ShowDialog(this)!=true)return null;
    return await worker.Call("replace",new{assetId=p.GetProperty("assetId").GetString(),path=dialog.FileName});
   }
   case "rebuild":{
    if(activeFiles.Length==0)throw new InvalidOperationException("No active package");
    VerifySnapshot(activeFiles);
    var testPath=args.Contains("--smoke")?args.FirstOrDefault(a=>a.StartsWith("--smoke-output=",StringComparison.Ordinal))?[15..]:null;
    if(testPath is not null)return await worker.Call("rebuild",new{output=Path.GetFullPath(testPath)});
    var dialog=new OpenFolderDialog{Multiselect=false,Title="Beta — rebuild output / 重建输出目录"};
    if(dialog.ShowDialog(this)!=true)return null;
    return await worker.Call("rebuild",new{output=dialog.FolderName});
   }
   case "neighbors":return await worker.Call("neighbors",p);
   case "preview":case "dump":case "bank":{
    var snapshot=activeFiles.ToArray();var version=generation;var ticket=previewEpoch;var cancellation=previewCancellation.Token;int request=++previewRequest;if(snapshot.Length==0)throw new InvalidOperationException("No active assets");
    await previewCleanup;
    await previewLane.WaitAsync();try{
     if(ticket!=previewEpoch||version!=generation||request!=previewRequest)throw new OperationCanceledException();VerifySnapshot(snapshot);
     var client=sharedHeavyLane?worker:previewWorker;
     if(!sharedHeavyLane&&previewGeneration!=version){await previewWorker.Call("load",new{paths=snapshot.Select(f=>f.Path).ToArray(),dependencyPaths=files.Select(f=>f.Path).ToArray()},cancellation);previewGeneration=version;}
     if(ticket!=previewEpoch||version!=generation||request!=previewRequest)throw new OperationCanceledException();
     object payload=method=="preview"?new{assetId=p.GetProperty("assetId").GetString(),output=previewCache}:p;
     var response=await client.Call(method,payload,cancellation);if(ticket!=previewEpoch||version!=generation)throw new IOException("Preview cancelled or workspace changed");
     if(method=="bank")return response;
     var asset=await client.Call("asset",new{assetId=p.GetProperty("assetId").GetString()},cancellation);
     if(ticket!=previewEpoch||version!=generation)throw new IOException("Preview cancelled or workspace changed");
     if(method=="dump")return new{text=response.GetProperty("text").GetString(),asset};
     var file=response.GetProperty("file").GetString()!;if(Path.GetFileName(file)!=file)throw new IOException("Invalid preview path");
     return new{url="https://preview.hok.local/"+Path.GetFileName(previewCache)+"/"+Uri.EscapeDataString(file),kind=response.GetProperty("kind").GetString(),asset,warnings=response.TryGetProperty("warnings",out var warnings)?warnings.Clone():(JsonElement?)null,animation=response.TryGetProperty("animation",out var animation)?animation.GetString():null};
    }finally{previewLane.Release();}
   }
   case "export":{
    var snapshot=activeFiles.ToArray();var ticket=exportEpoch;var version=generation;var shared=sharedHeavyLane;var cancellation=exportCancellation.Token;if(snapshot.Length==0)throw new InvalidOperationException("No active assets");
    var output=PickExportFolder();if(output is null)return null;
    await exportLane.WaitAsync(cancellation);try{if(ticket!=exportEpoch)throw new OperationCanceledException();VerifySnapshot(snapshot);if(!shared)await exportWorker.Call("load",new{paths=snapshot.Select(f=>f.Path).ToArray(),dependencyPaths=files.Select(f=>f.Path).ToArray()},cancellation);
     if(ticket!=exportEpoch)throw new OperationCanceledException();VerifySnapshot(snapshot);
     return await (shared?worker:exportWorker).Call("export",new{assetIds=p.GetProperty("assetIds"),format=p.GetProperty("format").GetString(),output,options=p.GetProperty("options")},cancellation);
    }finally{try{if(shared&&version==generation&&!closing)await worker.Call("releasePreview",new{});await exportWorker.StopAsync();await Task.Run(()=>OwnedCache.Clear(exportCache,cache));}finally{exportLane.Release();}}
   }
   case "exportList":{
    var dialog=new SaveFileDialog{Filter="JSON (*.json)|*.json",FileName="workspace-assets.json"};if(dialog.ShowDialog(this)!=true)return null;
    var all=new List<JsonElement>();int page=0,total=1;while(all.Count<total){var result=await worker.Call("list",new{page,query="",type=""});total=result.GetProperty("total").GetInt32();all.AddRange(result.GetProperty("items").EnumerateArray().Select(x=>x.Clone()));page++;}
    await File.WriteAllTextAsync(dialog.FileName,JsonSerializer.Serialize(new{created=DateTimeOffset.UtcNow,assets=all},App.Json));return new{count=all.Count,path=dialog.FileName};
   }
   case "logs":return new{text=string.Join("\n",new[]{"worker.log","preview.log","export.log"}.Where(n=>File.Exists(Path.Combine(data,n))).Select(n=>n+"\n"+string.Join('\n',File.ReadLines(Path.Combine(data,n)).TakeLast(80))))};
   default:throw new InvalidOperationException("Unsupported desktop operation");
  }
 }
 string? PickExportFolder(){
  var testPath=args.Contains("--smoke")?args.FirstOrDefault(a=>a.StartsWith("--smoke-output=",StringComparison.Ordinal))?[15..]:null;
  if(testPath is not null)return Path.GetFullPath(testPath);
  var dialog=new OpenFolderDialog{Multiselect=false};return dialog.ShowDialog(this)==true?dialog.FolderName:null;
 }
}
