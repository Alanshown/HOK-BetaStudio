using System.Buffers.Binary;
using System.Text;
namespace Hok.Worker;
internal static class WwiseIndex
{
    // Explicit package table entries and SoundBank DIDX ranges, never signature carving.
    public static IEnumerable<ResourceAsset> Expand(ResourceAsset parent)
    {
        if(parent.Extension=="pck"){
            var bytes=parent.Data;if(bytes.Length<28)throw new InvalidDataException("PCK header is truncated.");
            if(!bytes.AsSpan(0,4).SequenceEqual("AKPK"u8)||U32(bytes,8)!=1)throw new InvalidDataException("Unsupported PCK byte order or version; export the original package.");
            long headerEnd=8L+U32(bytes,4);if(headerEnd<28||headerEnd>bytes.Length)throw new InvalidDataException("PCK header size is invalid.");
            int languages=I32(bytes,12),banks=I32(bytes,16),streams=I32(bytes,20),external=I32(bytes,24);
            if(28L+languages+banks+streams+external>headerEnd)throw new InvalidDataException("PCK tables exceed the header.");
            int p=28;Range(bytes,p,languages);p+=languages;
            foreach(var table in new[]{("bank",banks),("stream",streams),("external",external)}){
                Range(bytes,p,table.Item2);if(table.Item2<4)throw new InvalidDataException("PCK table header is truncated.");
                int count=I32(bytes,p);int stride=table.Item1=="external"?24:20;
                if(count<0||count>100000||4L+(long)count*stride>table.Item2)throw new InvalidDataException("PCK table count is invalid.");
                for(int i=0;i<count;i++){
                    int at=p+4+i*stride;ulong id=table.Item1=="external"?U64(bytes,at):U32(bytes,at);int shift=table.Item1=="external"?4:0;
                    uint block=U32(bytes,at+4+shift),length=U32(bytes,at+8+shift),offset=U32(bytes,at+12+shift),language=U32(bytes,at+16+shift);
                    long begin=checked((long)block*offset);if(block==0||length>int.MaxValue||begin<headerEnd||begin>bytes.Length-length)throw new InvalidDataException("PCK entry is out of bounds.");
                    var data=bytes.AsSpan((int)begin,(int)length).ToArray();var detected=ResourceAsset.Detect(data,"");
                    string extension=detected.extension,type=detected.type;
                    if(table.Item1=="bank"&&extension!="bnk")throw new InvalidDataException("PCK bank does not start with BKHD.");
                    var child=new ResourceAsset(parent.Id+"/"+table.Item1+"/"+language+"/"+id,id+"."+extension,parent.Source+" / "+parent.Name,type,extension,data,parent.Id);
                    yield return child;
                    if(extension=="bnk")foreach(var media in Expand(child))yield return media;
                }
                p+=table.Item2;
            }
        }else if(parent.Extension=="bnk"){
            var bytes=parent.Data;int p=0,index=-1,indexSize=0,dataStart=-1,dataSize=0;
            while(p<bytes.Length){
                Range(bytes,p,8);int size=I32(bytes,p+4);Range(bytes,p+8,size);var tag=Encoding.ASCII.GetString(bytes,p,4);
                if(tag=="DIDX"){index=p+8;indexSize=size;}
                if(tag=="DATA"){dataStart=p+8;dataSize=size;}
                p+=8+size;
            }
            if(index<0)yield break;
            if(indexSize%12!=0||dataStart<0)throw new InvalidDataException("SoundBank media index is incomplete.");
            for(int i=0;i<indexSize;i+=12){
                uint id=U32(bytes,index+i),offset=U32(bytes,index+i+4),size=U32(bytes,index+i+8);
                if(size>int.MaxValue||offset>(long)dataSize-size)throw new InvalidDataException("SoundBank media range is invalid.");
                var data=bytes.AsSpan(checked(dataStart+(int)offset),(int)size).ToArray();
                yield return new ResourceAsset(parent.Id+"/wem/"+id,id+".wem",parent.Source+" / "+parent.Name,"WwiseAudio","wem",data,parent.Id);
            }
        }
    }
    static void Range(byte[] bytes,int offset,int count){if(offset<0||count<0||offset>bytes.Length-count)throw new InvalidDataException("Container range is invalid.");}
    static uint U32(byte[] b,int at){Range(b,at,4);return BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at));}
    static ulong U64(byte[] b,int at){Range(b,at,8);return BinaryPrimitives.ReadUInt64LittleEndian(b.AsSpan(at));}
    static int I32(byte[] b,int at)=>checked((int)U32(b,at));
}
