using System.IO.Compression;
namespace Hok.Worker;
internal static class BankArchive
{
    public static ResourceAsset[] Audio(ResourceAsset bank){
        if(!bank.IsBank)throw new InvalidOperationException("This resource is not a SoundBank.");
        // Fully validate the bank before creating a ZIP; never silently omit broken ranges.
        return WwiseIndex.Expand(bank).Where(r=>r.IsAudio).ToArray();
    }
    public static void Write(ResourceAsset bank,string format,string output){
        if(format is not ("zip-wem" or "zip-mp3"))throw new NotSupportedException(format);
        var audio=Audio(bank);
        if(audio.Length==0)throw new InvalidDataException("This BNK has no embedded audio. It may reference external media; export the original BNK instead.");
        string extension=format=="zip-mp3"?"mp3":"wem";
        var partial=output+"."+Guid.NewGuid().ToString("N")+".partial";
        try{
            using(var stream=new FileStream(partial,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            using(var archive=new ZipArchive(stream,ZipArchiveMode.Create)){
                var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach(var item in audio){
                    AudioTools.CheckCancellation();
                    string name=Path.GetFileNameWithoutExtension(item.Name),entryName=name+"."+extension;
                    if(name.Length==0||name.Any(c=>!char.IsAsciiDigit(c)))throw new InvalidDataException("Invalid SoundBank media ID.");
                    int duplicate=1;while(!names.Add(entryName))entryName=name+"_"+(++duplicate)+"."+extension;
                    string? converted=null;
                    try{
                        if(extension=="mp3"){
                            converted=Path.Combine(AudioTools.Scratch,Guid.NewGuid().ToString("N")+".mp3");
                            item.Write("mp3",converted);
                        }
                        using var dest=archive.CreateEntry(entryName,CompressionLevel.Optimal).Open();
                        if(converted is null){var bytes=item.Data;AudioValidation.ValidateWave(bytes,false);dest.Write(bytes);}
                        else{using var source=File.OpenRead(converted);source.CopyTo(dest);}
                    }catch(Exception e){throw new IOException($"{item.Name}: {e.GetBaseException().Message}; ZIP export was not completed.",e);}
                    finally{if(converted is not null&&File.Exists(converted))File.Delete(converted);}
                }
            }
            File.Move(partial,output,false);
        }finally{if(File.Exists(partial))File.Delete(partial);}
    }
}
