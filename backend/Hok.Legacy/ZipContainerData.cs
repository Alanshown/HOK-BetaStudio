using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace AssetStudio;

// Directory metadata and bounded prefixes only. Member content stays lazy;
// names (including duplicates and ../) are never used as extraction paths.
public static class ZipContainerData
{
    public sealed record Member(int Index,string Name,long Bytes,long CompressedBytes,uint Crc32,bool Directory,byte[] Head,string Error);
    public sealed record Index(string Kind,Member[] Members,bool IsNumpy);
    public static Index ReadIndex(Func<Stream> open)
    {
        using var source=open();using var zip=new ZipArchive(source,ZipArchiveMode.Read);
        if(zip.Entries.Count>100000)throw new InvalidDataException("ZIP member count exceeds supported limit");
        var result=new List<Member>();
        for(int i=0;i<zip.Entries.Count;i++)
        {
            var entry=zip.Entries[i];bool directory=entry.FullName.EndsWith("/",StringComparison.Ordinal);
            byte[] head=Array.Empty<byte>();string error=null;
            if(!directory)try{using var member=entry.Open();head=new byte[(int)Math.Min(256,entry.Length)];member.ReadExactly(head);}
            catch(Exception e){error=e.GetBaseException().Message;head=Array.Empty<byte>();}
            result.Add(new(i,entry.FullName,entry.Length,entry.CompressedLength,entry.Crc32,directory,head,error));
        }
        bool npy=result.Count>0&&result.All(m=>!m.Directory&&m.Name.EndsWith(".npy",StringComparison.OrdinalIgnoreCase));
        bool manifest=result.Count(m=>m.Name=="AndroidManifest.xml"&&m.Head.AsSpan().StartsWith(new byte[]{3,0,8,0}))==1;
        bool dex=result.Any(m=>m.Name=="classes.dex"&&m.Head.AsSpan().StartsWith(new byte[]{100,101,120,10}));
        return new(manifest&&dex?"AndroidPackage":"ZipArchive",result.ToArray(),npy);
    }
    public static object Describe(Func<Stream> open)
    {
        var index=ReadIndex(open);
        return new{schema="zip-members-v1",index.Kind,memberCount=index.Members.Length,
            structureComplete=index.Members.All(m=>m.Error==null),semanticComplete=false,
            members=index.Members.Select(m=>new{m.Index,m.Name,m.Bytes,m.CompressedBytes,m.Crc32,m.Directory,m.Error}),
            note="Directory and bounded member signatures; not a full interpretation of contained binaries. Native members are exported lazily with CRC validation. Duplicate names have separate member indexes; paths are never extracted verbatim."};
    }
    public static Stream OpenMember(Func<Stream> open,Member expected)
    {
        var source=open();ZipArchive zip=null;
        try
        {
            zip=new ZipArchive(source,ZipArchiveMode.Read);
            if(expected.Index<0||expected.Index>=zip.Entries.Count)throw new InvalidDataException("ZIP member index changed");
            var member=zip.Entries[expected.Index];
            if(member.FullName!=expected.Name||member.Length!=expected.Bytes||member.Crc32!=expected.Crc32)throw new InvalidDataException("ZIP member identity changed");
            return new CheckedMember(source,zip,member.Open(),member.Length,member.Crc32);
        }
        catch{zip?.Dispose();source.Dispose();throw;}
    }
    sealed class CheckedMember(Stream source,ZipArchive archive,Stream member,long length,uint expectedCrc):Stream
    {
        long position;uint crc=uint.MaxValue;bool verified;
        static readonly uint[] Table=BuildTable();
        static uint[] BuildTable(){var table=new uint[256];for(uint i=0;i<256;i++){uint c=i;for(int j=0;j<8;j++)c=(c&1)!=0?0xedb88320u^(c>>1):c>>1;table[i]=c;}return table;}
        public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>false;
        public override long Length=>length;public override long Position{get=>position;set=>throw new NotSupportedException();}
        public override int Read(byte[] buffer,int offset,int count)=>Read(buffer.AsSpan(offset,count));
        public override int Read(Span<byte> buffer)
        {
            int n=member.Read(buffer);if(position>length-n)throw new InvalidDataException("ZIP member exceeds declared length");
            foreach(byte b in buffer[..n])crc=Table[(crc^b)&255]^(crc>>8);position+=n;
            if(position==length&&!verified){if(~crc!=expectedCrc)throw new InvalidDataException("ZIP member CRC mismatch");verified=true;}
            if(n==0&&buffer.Length>0&&position!=length)throw new EndOfStreamException("ZIP member ended before declared length");
            return n;
        }
        protected override void Dispose(bool disposing){if(disposing){member.Dispose();archive.Dispose();source.Dispose();}base.Dispose(disposing);}
        public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();public override void Flush(){}
        public override void SetLength(long value)=>throw new NotSupportedException();public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
    }
}
