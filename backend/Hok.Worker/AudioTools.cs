using System.Diagnostics;
using System.Text;
namespace Hok.Worker;
internal static class AudioTools
{
    static string Tool(string name)=>Path.Combine(AppContext.BaseDirectory,"media",name);
    public static bool DecoderReady=>File.Exists(Tool("vgmstream-cli.exe"));
    public static bool Mp3Ready=>File.Exists(Tool("ffmpeg.exe"));
    public static string Scratch { get; set; }=Path.GetTempPath();
    public static void Decode(byte[] bytes,string extension,string output)
    {
        Directory.CreateDirectory(Scratch);
        var input=Path.Combine(Scratch,Guid.NewGuid().ToString("N")+"."+extension);
        try {
            File.WriteAllBytes(input,bytes);
            if(extension=="wem"||extension=="bnk"||extension=="fsb"){
                if(!DecoderReady)throw new NotSupportedException("Wwise decoder is not installed.");
                Run(Tool("vgmstream-cli.exe"),["-i","-o",output,input]);
            }else{
                if(!Mp3Ready)throw new NotSupportedException("Audio converter is not installed.");
                Run(Tool("ffmpeg.exe"),["-nostdin","-v","error","-y","-protocol_whitelist","file,pipe","-i",input,"-vn","-c:a","pcm_s16le",output]);
            }
            ValidateWave(output);
        }finally{if(File.Exists(input))File.Delete(input);}
    }
    public static void Mp3FromWave(string wave,string output)
    {
        if(!Mp3Ready)throw new NotSupportedException("MP3 encoder is not installed.");
        ValidateWave(wave);
        Run(Tool("ffmpeg.exe"),["-nostdin","-v","error","-y","-protocol_whitelist","file,pipe","-i",wave,"-map","0:a:0","-vn","-c:a","libmp3lame","-q:a","2",output]);
        if(!File.Exists(output)||new FileInfo(output).Length<32)throw new InvalidDataException("MP3 conversion produced no data.");
    }
    public static void ValidateWave(string path)
    {
        using var stream=File.OpenRead(path);Span<byte> header=stackalloc byte[12];
        if(stream.Read(header)!=12||!header[..4].SequenceEqual("RIFF"u8)||!header[8..12].SequenceEqual("WAVE"u8)||stream.Length<=44)
            throw new InvalidDataException("Decoder did not produce a playable WAV.");
    }
    static void Run(string executable,string[] arguments)
    {
        var info=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardOutput=true,WorkingDirectory=Path.GetDirectoryName(executable)!};
        foreach(var argument in arguments)info.ArgumentList.Add(argument);
        using var process=Process.Start(info)??throw new IOException("Audio process could not start.");
        var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
        var watch=Stopwatch.StartNew();
        while(!process.WaitForExit(100)){
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
