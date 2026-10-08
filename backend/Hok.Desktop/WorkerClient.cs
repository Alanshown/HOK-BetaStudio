using System.Diagnostics;
using System.Text.Json;
namespace Hok.Desktop;
internal sealed class WorkerClient : IDisposable {
 Process? process; readonly string cache; readonly string baseDir; readonly string logPath;
 readonly string audioScratch=Path.Combine(Path.GetTempPath(),"HokBetaStudioAudio",Guid.NewGuid().ToString("N"));
 readonly string cancellationName="Local\\HokBetaStudio-"+Guid.NewGuid().ToString("N");
 readonly EventWaitHandle cancellationEvent;
 // Three independent lanes exist. Keep their combined nominal budget at no
 // more than half the memory available to .NET; a 485k-object DB cannot fit
 // the former fixed 1.5 GiB cap even with disk-backed payloads/lazy materials.
 static readonly long memoryLimit=Math.Clamp(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes/6,1536L*1024*1024,4L*1024*1024*1024);
 readonly SemaphoreSlim gate=new(1,1); int epoch;
 public WorkerClient(string root,string cacheDir,string log){baseDir=root;cache=cacheDir;logPath=log;cancellationEvent=new(false,EventResetMode.ManualReset,cancellationName);}
 public void Cancel(){Interlocked.Increment(ref epoch);try{if(process is{HasExited:false})process.Kill(true);}catch(InvalidOperationException){}}
 public async Task<JsonElement> Call(string method,object payload,CancellationToken cancellationToken=default){
  int version=epoch;await gate.WaitAsync(cancellationToken);try{
   cancellationToken.ThrowIfCancellationRequested();cancellationEvent.Reset();
   using var registration=cancellationToken.Register(()=>cancellationEvent.Set());
   if(version!=epoch)throw new OperationCanceledException();
   if(process is null||process.HasExited){process?.Dispose();var exe=Path.Combine(baseDir,"worker","Hok.Worker.exe");
    var start=new ProcessStartInfo(exe){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=Path.GetDirectoryName(exe)!};
    start.StandardInputEncoding=start.StandardOutputEncoding=start.StandardErrorEncoding=new System.Text.UTF8Encoding(false);
    start.Environment["HOK_AUDIO_SCRATCH"]=audioScratch;
    start.Environment["HOK_CANCEL_EVENT"]=cancellationName;
    start.ArgumentList.Add(cache);process=Process.Start(start)??throw new IOException("Worker could not start");var active=process;
    _=Task.Run(async()=>{try{while(await active.StandardError.ReadLineAsync() is { } line){try{await File.AppendAllTextAsync(logPath,line+Environment.NewLine);}catch(IOException){}}}catch(ObjectDisposedException){}catch(IOException){}});
   }
   var p=process;var id=Guid.NewGuid().ToString("N");
   await p.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new{id,method,payload},App.Json));await p.StandardInput.FlushAsync();
   using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(3));
   var read=p.StandardOutput.ReadLineAsync(timeout.Token).AsTask();
   while(!read.IsCompleted){await Task.WhenAny(read,Task.Delay(400,timeout.Token));if(p.HasExited&&!read.IsCompleted)break;p.Refresh();if(!p.HasExited&&p.PrivateMemorySize64>memoryLimit){Cancel();throw new IOException($"Worker memory limit exceeded ({memoryLimit/1024/1024} MiB, sized for this computer). Open fewer DB files at once.");}}
   var line=await read;if(version!=epoch)throw new OperationCanceledException();
   if(line is null)throw new IOException("Worker exited. The desktop window is still available; retry this file.");
   if(line.Length>16_000_000)throw new IOException("Worker response exceeds limit");
   using var doc=JsonDocument.Parse(line);var response=doc.RootElement;if(response.GetProperty("id").GetString()!=id)throw new IOException("Worker protocol mismatch");
   if(!response.GetProperty("ok").GetBoolean())throw new IOException(response.GetProperty("error").GetProperty("message").GetString());
   return response.GetProperty("data").Clone();
  }catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested){throw new IOException("Operation cancelled");}
   catch(OperationCanceledException){Cancel();throw new IOException("Operation cancelled or timed out");}
   finally{gate.Release();}
 }
 public void Dispose(){Cancel();try{process?.WaitForExit(3000);ClearAudioScratch();}catch(IOException){}catch(InvalidOperationException){}finally{process?.Dispose();cancellationEvent.Dispose();}}
 void ClearAudioScratch()=>OwnedCache.Clear(audioScratch,Path.Combine(Path.GetTempPath(),"HokBetaStudioAudio"));
 public async Task StopAsync(){
  Cancel();await gate.WaitAsync();
  try{
   if(process is not null){if(!process.HasExited){process.Kill(true);await process.WaitForExitAsync();}process.Dispose();process=null;}
   ClearAudioScratch();
  }finally{gate.Release();}
 }
}
