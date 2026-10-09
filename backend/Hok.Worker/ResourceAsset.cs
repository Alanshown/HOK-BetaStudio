using System.Text;
using SixLabors.ImageSharp;
namespace Hok.Worker;
internal sealed record ResourceAsset(string Id,string Name,string Source,string Type,string Extension,byte[] Bytes,string? Parent=null,string? Warning=null,bool RawAvailable=true,long? DeclaredBytes=null,AssetStudio.ContainerEntry? Backing=null,ResourceAsset? SliceParent=null,long SliceOffset=0,long? SliceLength=null)
{
    public static long MaterializedBytes {get;private set;}
    public static void ResetMetrics()=>MaterializedBytes=0;
    public Stream Open()=>SliceParent is not null?new ResourceSliceStream(SliceParent.Open(),SliceOffset,SliceLength??throw new InvalidDataException("Missing resource member length")):Backing?.Open()??new MemoryStream(Bytes,false);
    public byte[] Data {get{using var input=Open();var result=new byte[checked((int)input.Length)];input.ReadExactly(result);MaterializedBytes+=result.Length;return result;}}
    public long Length=>SliceLength??Backing?.Length??Bytes.Length;
    public byte[] Head(int count=256){using var input=Open();var bytes=new byte[(int)Math.Min(count,input.Length)];input.ReadExactly(bytes);return bytes;}
    public void CopyTo(Stream output){using var input=Open();input.CopyTo(output);}
    public bool IsAudio=>Type is "WwiseAudio" or "AudioFile";
    public bool IsBank=>Type=="WwiseBank";
    public bool IsStructured=>Type is "HokObjectTree" or "HokActionTimeline" or "XmlAsset" or "TextFile" or "QtsChecksumManifest" or "QtsRoutingCatalog" or "StdrTable" or "QtsKeyValueDatabase" or "QtsTypeTreeDatabase" or "QtsResourceMap" or "QtsScriptDependencies" or "NumpyArchive" or "NumpyArray" or "ZipArchive" or "AndroidPackage";
    public string Preview=>IsBank?"bank":IsAudio?"audio":Type=="ImageFile"?"image":"data";
    public string[] Formats=>!RawAvailable?new[]{"json"}:IsStructured?(Type=="HokObjectTree"?new[]{"json","xml","original","raw"}:new[]{"original","json","raw"}):Type=="ImageFile"?new[]{"original",Extension,"png","json","raw"}.Distinct().ToArray():IsBank
        ? new[]{"original","zip-wem"}.Concat(AudioTools.DecoderReady&&AudioTools.Mp3Ready?new[]{"zip-mp3"}:[]).Concat(new[]{"raw","json"}).ToArray()
        : IsAudio
        ? new[]{"original",Extension}.Concat(AudioTools.DecoderReady||Type=="AudioFile"?new[]{"wav"}:[]).Concat(AudioTools.Mp3Ready&&(AudioTools.DecoderReady||Type=="AudioFile")?new[]{"mp3"}:[]).Append("raw").Distinct().ToArray()
        : new[]{"original","raw","json"};
    public string ExportExtension(string format)=>format.StartsWith("zip-",StringComparison.Ordinal)?"zip":format is "original" or "raw"?Extension:format;
    public void Write(string format,string output)
    {
        if(!Formats.Contains(format))throw new NotSupportedException(format);
        if(IsBank&&format.StartsWith("zip-",StringComparison.Ordinal)){BankArchive.Write(this,format,output);return;}
        if(format is "original" or "raw"||format==Extension){
            if((Backing is not null||SliceParent is not null)&&(!IsAudio&&!IsBank||format=="raw")){using var outputFile=File.Create(output);CopyTo(outputFile);return;}
            var bytes=Data;
            if(format!="raw"){
                if(IsBank)AudioValidation.ValidateBank(bytes);
                if(Extension=="wem")AudioValidation.ValidateWave(bytes,false);
                if(Extension=="wav")AudioValidation.ValidateWave(bytes,true);
            }
            if(IsAudio||IsBank)AudioTools.WriteOriginal(bytes,output);else File.WriteAllBytes(output,bytes);return;
        }
        if(IsStructured&&format is "json" or "xml"){HokStructured.Write(this,format,output);return;}
        if(Type=="ImageFile"&&format=="png"){using var input=Open();using var image=Image.Load(input);image.SaveAsPng(output);return;}
        if(format=="json"){File.WriteAllText(output,System.Text.Json.JsonSerializer.Serialize(Describe(),Program.Json));return;}
        if(format=="wav"){AudioTools.Decode(Data,Extension,output);return;}
        if(format=="mp3"){
            var wave=Path.Combine(AudioTools.Scratch,Guid.NewGuid().ToString("N")+".wav");
            try{AudioTools.Decode(Data,Extension,wave);AudioTools.Mp3FromWave(wave,output);}finally{if(File.Exists(wave))File.Delete(wave);}
            return;
        }
        throw new NotSupportedException(format);
    }
    public object Describe()=>new{Id,Name,Source,Type,Extension,bytes=DeclaredBytes??Length,retainedBytes=Bytes.Length,RawAvailable,Parent,Warning,head=Convert.ToHexString(Head())};
    public static (string type,string extension) Detect(byte[] bytes,string fallback,long? length=null)
    {
        var s=bytes.AsSpan();
        if(fallback=="NumpyArchive")return("NumpyArchive","npz");
        if(fallback=="ZipArchive")return("ZipArchive","zip");
        if(fallback=="AndroidPackage")return("AndroidPackage","apk");
        if(fallback=="NumpyArray"||s.StartsWith(new byte[]{147,78,85,77,80,89}))return("NumpyArray","npy");
        if(fallback is "QtsKeyValueDatabase" or "QtsTypeTreeDatabase" or "QtsResourceMap" or "QtsScriptDependencies")return(fallback,"db");
        if(fallback=="QtsRawMetadata")return("QtsRawMetadata","bin");
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
        if(s.Length>=12&&s[..4].SequenceEqual("RIFF"u8)&&s.Slice(8,4).SequenceEqual("WEBP"u8))return("ImageFile","webp");
        if(s.StartsWith("GIF87a"u8)||s.StartsWith("GIF89a"u8))return("ImageFile","gif");
        if(s.StartsWith("dex\n"u8)&&s.Length>=8&&s[7]==0)return("AndroidDex","dex");
        if(s.StartsWith(new byte[]{127,69,76,70}))return("ElfBinary","so");
        if(s.Length>=8&&s[..4].SequenceEqual(new byte[]{3,0,8,0})&&System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(s[4..])==length)return("AndroidBinaryXml","xml");
        if(s.Length>=12&&s[..4].SequenceEqual(new byte[]{2,0,12,0})&&System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(s[4..])==length)return("AndroidResourceTable","arsc");
        if(s.Length>=26&&s.StartsWith("BM"u8)&&System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(s.Slice(2))==(length??s.Length))return("ImageFile","bmp");
        if(s.StartsWith("QTSF_PACKAGE"u8))return("PackageMetadata","bytes");
        if(fallback=="AssetsFile")return("SerializedFile","assets");
        if(HokStructured.Detect(bytes,length??bytes.Length) is{} structured)return structured;
        return("ResourceFile","bin");
    }
    static ushort WaveTag(ReadOnlySpan<byte> data){
        int p=12;while(p+8<=data.Length){uint size=System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(p+4));if(size>data.Length-p-8)break;if(data.Slice(p,4).SequenceEqual("fmt "u8)&&size>=2)return System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(p+8));p+=checked(8+(int)size+((int)size&1));}return 0;
    }
}
