using System.Buffers.Binary;
namespace Hok.Worker;

internal static class AudioValidation
{
    internal readonly record struct WaveInfo(int Channels,int SampleRate,int Bits,long DataBytes);
    static uint U32(ReadOnlySpan<byte> bytes,int offset,bool big=false)=>big?BinaryPrimitives.ReadUInt32BigEndian(bytes[offset..]):BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]);
    static ushort U16(ReadOnlySpan<byte> bytes,int offset,bool big=false)=>big?BinaryPrimitives.ReadUInt16BigEndian(bytes[offset..]):BinaryPrimitives.ReadUInt16LittleEndian(bytes[offset..]);

    public static WaveInfo ValidateWave(ReadOnlySpan<byte> bytes,bool requirePcm)
    {
        bool big=bytes.StartsWith("RIFX"u8);
        if(bytes.Length<12||(!big&&!bytes.StartsWith("RIFF"u8))||!bytes.Slice(8,4).SequenceEqual("WAVE"u8))
            throw new InvalidDataException("Audio is not a RIFF WAVE stream.");
        if(8L+U32(bytes,4,big)!=bytes.Length)
            throw new InvalidDataException("Audio stream is truncated or its RIFF length is invalid. A streamed/prefetch WEM requires the complete media file; raw export remains available.");
        int p=12,format=-1,formatBytes=0;long dataBytes=0;bool foundData=false;
        while(p<bytes.Length){
            if(bytes.Length-p<8)throw new InvalidDataException("Audio chunk header is truncated.");
            uint length=U32(bytes,p+4,big);
            if(length>bytes.Length-p-8)throw new InvalidDataException("Audio chunk exceeds its stream.");
            var tag=bytes.Slice(p,4);
            if(tag.SequenceEqual("fmt "u8)){if(format>=0)throw new InvalidDataException("Duplicate audio format chunk.");format=p+8;formatBytes=(int)length;}
            if(tag.SequenceEqual("data"u8)){foundData=true;dataBytes+=length;}
            p+=8+(int)length;
            // Wwise permits an unpadded final odd-sized data chunk.
            if((length&1)!=0&&p<bytes.Length)p++;
        }
        if(format<0||formatBytes<16||!foundData||dataBytes==0)throw new InvalidDataException("Audio format or sample data is missing.");
        int channels=U16(bytes,format+2,big),sampleRate=checked((int)U32(bytes,format+4,big)),bits=U16(bytes,format+14,big);
        if(channels is <1 or >64||sampleRate is <1 or >768000)throw new InvalidDataException("Audio channel count or sample rate is invalid.");
        if(requirePcm){
            int tag=U16(bytes,format,big),alignment=U16(bytes,format+12,big);
            if(tag==0xfffe){
                if(big||formatBytes<40||U16(bytes,format+16)<22||!bytes.Slice(format+26,14).SequenceEqual(new byte[]{0,0,0,0,16,0,128,0,0,170,0,56,155,113}))
                    throw new InvalidDataException("Invalid extensible PCM format.");
                tag=U16(bytes,format+24);
            }
            if((tag!=1&&tag!=3)||bits==0||bits%8!=0||bits>64||alignment!=channels*(bits/8)||dataBytes%alignment!=0||U32(bytes,format+8,big)!=(long)sampleRate*alignment)
                throw new InvalidDataException("Decoder output does not contain complete PCM samples.");
        }
        return new(channels,sampleRate,bits,dataBytes);
    }

    public static void ValidateBank(byte[] bytes)
    {
        if(bytes.Length<16||!bytes.AsSpan().StartsWith("BKHD"u8))throw new InvalidDataException("SoundBank header is missing.");
        // Expand validates every bank chunk and DIDX/DATA range, including the
        // trailing sections. A valid event-only bank need not contain media.
        if(U32(bytes,4)<8)throw new InvalidDataException("SoundBank header is truncated.");
        _=WwiseIndex.Expand(new ResourceAsset("validation","bank.bnk","validation","WwiseBank","bnk",bytes)).ToArray();
    }
}
