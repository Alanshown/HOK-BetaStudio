using System.Text;
namespace Hok.Worker;
internal sealed record ResourceAsset(string Id,string Name,string Source,string Type,string Extension,byte[] Data,string? Parent=null,string? Warning=null)
{
    public bool IsAudio=>Type is "WwiseAudio" or "AudioFile";
    public bool IsBank=>Type=="WwiseBank";
    public string Preview=>IsBank?"bank":IsAudio?"audio":Extension is "png" or "jpg" or "bmp"?"image":"data";
    public string[] Formats=>IsBank
        ? new[]{"original","zip-wem"}.Concat(AudioTools.DecoderReady&&AudioTools.Mp3Ready?new[]{"zip-mp3"}:[]).Concat(new[]{"raw","json"}).ToArray()
        : IsAudio
        ? new[]{"original",Extension}.Concat(AudioTools.DecoderReady||Type=="AudioFile"?new[]{"wav"}:[]).Concat(AudioTools.Mp3Ready&&(AudioTools.DecoderReady||Type=="AudioFile")?new[]{"mp3"}:[]).Append("raw").Distinct().ToArray()
        : new[]{"original","raw","json"};
    public string ExportExtension(string format)=>format.StartsWith("zip-",StringComparison.Ordinal)?"zip":format is "original" or "raw"?Extension:format;
    public void Write(string format,string output)
    {
        if(!Formats.Contains(format))throw new NotSupportedException(format);
        if(IsBank&&format.StartsWith("zip-",StringComparison.Ordinal)){BankArchive.Write(this,format,output);return;}
        if(format is "original" or "raw"||format==Extension){File.WriteAllBytes(output,Data);return;}
        if(format=="json"){File.WriteAllText(output,System.Text.Json.JsonSerializer.Serialize(Describe(),Program.Json));return;}
        if(format=="wav"){AudioTools.Decode(Data,Extension,output);return;}
        if(format=="mp3"){
            var wave=Path.Combine(AudioTools.Scratch,Guid.NewGuid().ToString("N")+".wav");
            try{AudioTools.Decode(Data,Extension,wave);AudioTools.Mp3FromWave(wave,output);}finally{if(File.Exists(wave))File.Delete(wave);}
            return;
        }
        throw new NotSupportedException(format);
    }
    public object Describe()=>new{Id,Name,Source,Type,Extension,bytes=Data.Length,Parent,Warning,head=Convert.ToHexString(Data.AsSpan(0,Math.Min(256,Data.Length)))};
    public static (string type,string extension) Detect(byte[] bytes,string fallback)
    {
        var s=bytes.AsSpan();
        if(fallback=="CompressedQtsChunk")return("CompressedQtsChunk","qtschunk");
        if(s.StartsWith("AKPK"u8))return("WwisePackage","pck");
        if(s.StartsWith("BKHD"u8))return("WwiseBank","bnk");
        if(s.Length>=12&&s[..4].SequenceEqual("RIFF"u8)&&s.Slice(8,4).SequenceEqual("WAVE"u8)){
            var tag=WaveTag(s);return tag is 1 or 3?("AudioFile","wav"):("WwiseAudio","wem");
        }
        if(s.StartsWith("OggS"u8))return("AudioFile","ogg");
        if(s.StartsWith("fLaC"u8))return("AudioFile","flac");
        if(s.StartsWith("ID3"u8))return("AudioFile","mp3");
        if(s.Length>=4&&s[0]==0xFF&&(s[1]&0xE0)==0xE0&&(s[1]&0x06)!=0&&(s[1]&0x18)!=0x08&&(s[2]&0xF0) is not (0 or 0xF0)&&(s[2]&0x0C)!=0x0C)return("AudioFile","mp3");
        if(s.StartsWith("FSB4"u8)||s.StartsWith("FSB5"u8))return("AudioFile","fsb");
        if(s.StartsWith(new byte[]{137,80,78,71,13,10,26,10}))return("ImageFile","png");
        if(s.StartsWith(new byte[]{255,216,255}))return("ImageFile","jpg");
        if(s.Length>=26&&s.StartsWith("BM"u8)&&System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(s.Slice(2))==s.Length)return("ImageFile","bmp");
        if(s.StartsWith("QTSF_PACKAGE"u8))return("PackageMetadata","bytes");
        if(fallback=="AssetsFile")return("SerializedFile","assets");
        return("ResourceFile","bin");
    }
    static ushort WaveTag(ReadOnlySpan<byte> data){
        int p=12;while(p+8<=data.Length){uint size=System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(p+4));if(size>data.Length-p-8)break;if(data.Slice(p,4).SequenceEqual("fmt "u8)&&size>=2)return System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(p+8));p+=checked(8+(int)size+((int)size&1));}return 0;
    }
}
