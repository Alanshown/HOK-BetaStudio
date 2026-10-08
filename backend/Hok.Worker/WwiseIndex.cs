using System.Buffers.Binary;
using System.Text;
namespace Hok.Worker;
internal static class WwiseIndex
{
    // Index headers/tables only. Unselected media retains parent ranges, not byte arrays.
    public static IEnumerable<ResourceAsset> Expand(ResourceAsset parent)
    {
        using var input=parent.Open();
        ResourceAsset Member(string id,string name,string type,string extension,long offset,long length)=>
            new(id,name,parent.Source+" / "+parent.Name,type,extension,[],parent.Id,SliceParent:parent,SliceOffset:offset,SliceLength:length);
        if(parent.Extension=="pck"){
            if(input.Length<28)throw new InvalidDataException("PCK header is truncated.");
            if(Tag(input,0)!="AKPK"||U32(input,8)!=1)throw new InvalidDataException("Unsupported PCK byte order or version; export the original package.");
            long headerEnd=8L+U32(input,4);if(headerEnd<28||headerEnd>input.Length)throw new InvalidDataException("PCK header size is invalid.");
            int languages=I32(input,12),banks=I32(input,16),streams=I32(input,20),external=I32(input,24);
            if(28L+languages+banks+streams+external>headerEnd)throw new InvalidDataException("PCK tables exceed the header.");
            long p=28;Range(input,p,languages);p+=languages;
            foreach(var table in new[]{("bank",banks),("stream",streams),("external",external)}){
                Range(input,p,table.Item2);if(table.Item2<4)throw new InvalidDataException("PCK table header is truncated.");
                int count=I32(input,p),stride=table.Item1=="external"?24:20;
                if(count>100000||4L+(long)count*stride>table.Item2)throw new InvalidDataException("PCK table count is invalid.");
                for(int i=0;i<count;i++){
                    long at=p+4+i*stride;ulong id=table.Item1=="external"?U64(input,at):U32(input,at);int shift=table.Item1=="external"?4:0;
                    uint block=U32(input,at+4+shift),length=U32(input,at+8+shift),offset=U32(input,at+12+shift),language=U32(input,at+16+shift);
                    long begin=checked((long)block*offset);if(block==0||length>int.MaxValue||begin<headerEnd||begin>input.Length-length)throw new InvalidDataException("PCK entry is out of bounds.");
                    var child=Member(parent.Id+"/"+table.Item1+"/"+language+"/"+id,id+".bin","ResourceFile","bin",begin,length);
                    var detected=ResourceAsset.Detect(child.Head(),"",length);child=child with {Name=id+"."+detected.extension,Type=detected.type,Extension=detected.extension};
                    if(table.Item1=="bank"&&child.Extension!="bnk")throw new InvalidDataException("PCK bank does not start with BKHD.");
                    yield return child;
                    if(child.Extension=="bnk")foreach(var media in Expand(child))yield return media;
                }
                p+=table.Item2;
            }
        }else if(parent.Extension=="bnk"){
            long p=0,index=-1,dataStart=-1;int indexSize=0,dataSize=0;bool header=false;
            while(p<input.Length){
                Range(input,p,8);int size=I32(input,p+4);Range(input,p+8,size);string tag=Tag(input,p);
                if(p==0&&tag!="BKHD")throw new InvalidDataException("SoundBank BKHD header is missing.");
                if(tag=="BKHD"){if(header||size<8)throw new InvalidDataException("SoundBank BKHD header is invalid.");header=true;}
                if(tag=="DIDX"){if(index>=0)throw new InvalidDataException("Duplicate SoundBank media index.");index=p+8;indexSize=size;}
                if(tag=="DATA"){if(dataStart>=0)throw new InvalidDataException("Duplicate SoundBank media data.");dataStart=p+8;dataSize=size;}
                p+=8L+size;
            }
            if(!header)throw new InvalidDataException("SoundBank BKHD header is missing.");
            if(index<0)yield break;
            if(indexSize%12!=0||dataStart<0)throw new InvalidDataException("SoundBank media index is incomplete.");
            var ids=new HashSet<uint>();
            for(int i=0;i<indexSize;i+=12){
                uint id=U32(input,index+i),offset=U32(input,index+i+4),size=U32(input,index+i+8);
                if(!ids.Add(id))throw new InvalidDataException("Duplicate SoundBank media ID: "+id);
                if(size>int.MaxValue||offset>(long)dataSize-size)throw new InvalidDataException("SoundBank media range is invalid.");
                yield return Member(parent.Id+"/wem/"+id,id+".wem","WwiseAudio","wem",dataStart+offset,size);
            }
        }
    }
    static void Range(Stream input,long at,long size){if(at<0||size<0||at>input.Length-size)throw new InvalidDataException("Container range is invalid.");}
    static uint U32(Stream input,long at){Range(input,at,4);input.Position=at;Span<byte> bytes=stackalloc byte[4];input.ReadExactly(bytes);return BinaryPrimitives.ReadUInt32LittleEndian(bytes);}
    static ulong U64(Stream input,long at){Range(input,at,8);input.Position=at;Span<byte> bytes=stackalloc byte[8];input.ReadExactly(bytes);return BinaryPrimitives.ReadUInt64LittleEndian(bytes);}
    static int I32(Stream input,long at){uint value=U32(input,at);if(value>int.MaxValue)throw new InvalidDataException("Container count exceeds supported range");return (int)value;}
    static string Tag(Stream input,long at){Range(input,at,4);input.Position=at;Span<byte> bytes=stackalloc byte[4];input.ReadExactly(bytes);return Encoding.ASCII.GetString(bytes);}
}
