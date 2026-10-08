using System.Diagnostics;
using System.Text;
namespace Hok.Worker;
internal static class AudioTools
{
    static string Tool(string name)=>Path.Combine(AppContext.BaseDirectory,"media",name);
    public static bool DecoderReady=>File.Exists(Tool("vgmstream-cli.exe"));
    public static bool Mp3Ready=>File.Exists(Tool("ffmpeg.exe"));
    public static string Scratch { get; set; }=Path.GetTempPath();
    public static Action CheckCancellation {get;set;}=()=>{};
    public static void WriteOriginal(byte[] bytes,string output){
        var partial=output+"."+Guid.NewGuid().ToString("N")+".partial";
        try{File.WriteAllBytes(partial,bytes);File.Move(partial,output,true);}
        finally{if(File.Exists(partial))File.Delete(partial);}
    }
    public static void Decode(byte[] bytes,string extension,string output)
    {
        // Some native decoders silently accept, but cannot write, paths over
        // MAX_PATH. Use short relative ASCII names and move the validated output
        // with .NET, which supports the user's long/Unicode destination path.
        var ownedScratch=Environment.GetEnvironmentVariable("HOK_AUDIO_SCRATCH");
        var staging=string.IsNullOrEmpty(ownedScratch)?Directory.CreateTempSubdirectory("hok-audio-").FullName:Path.Combine(ownedScratch,Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        var input=Path.Combine(staging,"input."+extension);
        var partial=Path.Combine(staging,"decoded.wav");
        try {
            if(extension=="wem")AudioValidation.ValidateWave(bytes,false);
            File.WriteAllBytes(input,bytes);
            if(extension=="wem"||extension=="bnk"||extension=="fsb"){
                if(!DecoderReady)throw new NotSupportedException("Wwise decoder is not installed.");
                Run(Tool("vgmstream-cli.exe"),["-i","-D","2","-o",Path.GetFileName(partial),Path.GetFileName(input)],staging);
            }else{
                if(!Mp3Ready)throw new NotSupportedException("Audio converter is not installed.");
                Run(Tool("ffmpeg.exe"),["-nostdin","-v","error","-y","-protocol_whitelist","file,pipe","-i",Path.GetFileName(input),"-vn","-c:a","pcm_s16le",Path.GetFileName(partial)],staging);
            }
            ValidateWave(partial);
            File.Move(partial,output,true);
        }finally{if(File.Exists(input))File.Delete(input);if(File.Exists(partial))File.Delete(partial);Directory.Delete(staging);}
    }
    public static void Mp3FromWave(string wave,string output)
    {
        if(!Mp3Ready)throw new NotSupportedException("MP3 encoder is not installed.");
        ValidateWave(wave);
        var partial=output+"."+Guid.NewGuid().ToString("N")+".partial.mp3";
        try{
            // MPEG-1 Layer III, 44.1 kHz stereo, fixed bitrate and ID3v2.3 are
            // broadly compatible, including players with limited VBR support.
            Run(Tool("ffmpeg.exe"),["-nostdin","-v","error","-y","-protocol_whitelist","file,pipe","-i",wave,"-map","0:a:0","-map_metadata","-1","-vn","-ac","2","-ar","44100","-c:a","libmp3lame","-b:a","192k","-id3v2_version","3","-write_id3v1","1",partial]);
            ValidateMp3(partial);
            File.Move(partial,output,true);
        }finally{if(File.Exists(partial))File.Delete(partial);}
    }
    public static void ValidateMp3(string path){
        if(!File.Exists(path)||new FileInfo(path).Length<32)throw new InvalidDataException("MP3 conversion produced no data.");
        Run(Tool("ffmpeg.exe"),["-nostdin","-v","error","-xerror","-err_detect","explode","-protocol_whitelist","file,pipe","-i",path,"-map","0:a:0","-f","null","-"]);
    }
    public static void ValidateWave(string path)
    {
        if(!File.Exists(path))throw new InvalidDataException("Audio decoder produced no WAV output.");
        if(new FileInfo(path).Length>512L*1024*1024)throw new InvalidDataException("Decoded audio exceeds the safe size limit.");
        AudioValidation.ValidateWave(File.ReadAllBytes(path),true);
    }
    static void Run(string executable,string[] arguments,string? workingDirectory=null)
    {
        var info=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardOutput=true,WorkingDirectory=workingDirectory??Path.GetDirectoryName(executable)!};
        foreach(var argument in arguments)info.ArgumentList.Add(argument);
        using var process=Process.Start(info)??throw new IOException("Audio process could not start.");
        var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
        var watch=Stopwatch.StartNew();
        while(!process.WaitForExit(100)){
            try{CheckCancellation();}catch(OperationCanceledException){if(!process.HasExited)process.Kill(true);process.WaitForExit();throw;}
            long memory;
            try{process.Refresh();if(process.HasExited)break;memory=process.PrivateMemorySize64;}
            catch(InvalidOperationException) when(process.HasExited){break;}
            catch(System.ComponentModel.Win32Exception) when(process.HasExited){break;}
            if(watch.Elapsed.TotalSeconds>90||memory>768L*1024*1024){
                try{if(!process.HasExited)process.Kill(true);}catch(InvalidOperationException) when(process.HasExited){}
                process.WaitForExit();throw new IOException("Audio conversion exceeded its time or memory budget.");
            }
        }
        Task.WaitAll(stdout,stderr);
        if(process.ExitCode!=0)throw new IOException("Audio conversion failed: "+stderr.Result[..Math.Min(2000,stderr.Result.Length)]);
    }
}
