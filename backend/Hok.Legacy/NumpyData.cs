using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AssetStudio;

// NPY format: https://numpy.org/doc/stable/reference/generated/numpy.lib.format.html
// Only primitive numeric arrays are interpreted. Never evaluate Python/pickle.
public static class NumpyData
{
    public sealed record Header(string Descriptor,bool FortranOrder,long[] Shape,long Elements,int ItemBytes,long DataOffset,long DataBytes);
    public sealed record Member(string Name,long Bytes,Header Header);
    static readonly byte[] Magic={147,78,85,77,80,89};
    static Match Match(string text,string pattern)=>Regex.Match(text,pattern,RegexOptions.CultureInvariant,TimeSpan.FromSeconds(1));
    public static Header ReadHeader(Stream stream,long length)
    {
        Span<byte> prefix=stackalloc byte[12];stream.ReadExactly(prefix[..8]);
        if(!prefix[..6].SequenceEqual(Magic)||prefix[7]!=0||prefix[6] is not (1 or 2 or 3))throw new InvalidDataException("Unsupported NPY signature/version");
        int sizeWord=prefix[6]==1?2:4;stream.ReadExactly(prefix.Slice(8,sizeWord));
        uint headerBytes=sizeWord==2?BinaryPrimitives.ReadUInt16LittleEndian(prefix[8..]):BinaryPrimitives.ReadUInt32LittleEndian(prefix[8..]);
        if(headerBytes==0||headerBytes>1024*1024||headerBytes>length-8-sizeWord)throw new InvalidDataException("NPY header range");
        byte[] raw=new byte[headerBytes];stream.ReadExactly(raw);
        string text=(prefix[6]==3?new UTF8Encoding(false,true):Encoding.Latin1).GetString(raw);
        if(!text.EndsWith('\n'))throw new InvalidDataException("NPY header newline missing");
        var dtype=Match(text,"['\"]descr['\"]\\s*:\\s*['\"](?<value>[<>=|][fiub][0-9]+)['\"]");
        var order=Match(text,"['\"]fortran_order['\"]\\s*:\\s*(?<value>True|False)");
        var shape=Match(text,"['\"]shape['\"]\\s*:\\s*\\((?<value>[0-9, \\t]*)\\)");
        if(!dtype.Success||!order.Success||!shape.Success)throw new InvalidDataException("Unsupported NPY header/dtype; object arrays are not executed");
        var rest=text;foreach(var match in new[]{dtype,order,shape}.OrderByDescending(m=>m.Index))rest=rest.Remove(match.Index,match.Length);
        if(rest.Any(c=>!char.IsWhiteSpace(c)&&c is not ('{' or '}' or ',')))throw new InvalidDataException("Unexpected NPY header fields");
        string descriptor=dtype.Groups["value"].Value;int width=int.Parse(descriptor[2..],CultureInfo.InvariantCulture);char kind=descriptor[1];
        if(kind=='f'&&width is not (2 or 4 or 8)||kind is 'i' or 'u'&&width is not (1 or 2 or 4 or 8)||kind=='b'&&width!=1)throw new InvalidDataException("Unsupported NPY scalar width");
        var dimensions=shape.Groups["value"].Value.Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Select(s=>long.Parse(s,CultureInfo.InvariantCulture)).ToArray();
        long elements=1;foreach(long dimension in dimensions)elements=checked(elements*dimension);
        long offset=8+sizeWord+headerBytes,dataBytes=checked(elements*width);
        if(offset>length||dataBytes!=length-offset)throw new InvalidDataException("NPY shape/dtype does not cover its exact payload");
        return new(descriptor,order.Groups["value"].Value=="True",dimensions,elements,width,offset,dataBytes);
    }
    public static Member[] ReadArchive(Func<Stream> open)
    {
        using var source=open();using var zip=new ZipArchive(source,ZipArchiveMode.Read);
        if(zip.Entries.Count==0||zip.Entries.Count>100000)throw new InvalidDataException("NPZ entry count outside supported bounds");
        var names=new HashSet<string>(StringComparer.Ordinal);var result=new List<Member>();
        foreach(var entry in zip.Entries)
        {
            if(!entry.FullName.EndsWith(".npy",StringComparison.OrdinalIgnoreCase)||!names.Add(entry.FullName))throw new InvalidDataException("ZIP is not an unambiguous numeric NPZ archive");
            using var member=entry.Open();result.Add(new(entry.FullName,entry.Length,ReadHeader(member,entry.Length)));
        }
        return result.ToArray();
    }
    public static Stream OpenMember(Func<Stream> open,string name,long expectedBytes)
    {
        var source=open();ZipArchive zip=null;
        try{zip=new ZipArchive(source,ZipArchiveMode.Read);var entry=zip.GetEntry(name)??throw new InvalidDataException("NPZ member disappeared");
            if(entry.Length!=expectedBytes)throw new InvalidDataException("NPZ member length changed");return new MemberStream(source,zip,entry.Open(),entry.Length);}
        catch{zip?.Dispose();source.Dispose();throw;}
    }
    public static object Describe(Func<Stream> open,long length,int sampleLimit=64)
    {
        using var stream=open();var header=ReadHeader(stream,length);int count=(int)Math.Min(header.Elements,sampleLimit);var values=new object[count];
        Span<byte> value=stackalloc byte[8];bool big=header.Descriptor[0]=='>';char kind=header.Descriptor[1];
        for(int i=0;i<count;i++)
        {
            var bytes=value[..header.ItemBytes];stream.ReadExactly(bytes);if(big)bytes.Reverse();
            ulong bits=header.ItemBytes switch{1=>bytes[0],2=>BinaryPrimitives.ReadUInt16LittleEndian(bytes),4=>BinaryPrimitives.ReadUInt32LittleEndian(bytes),8=>BinaryPrimitives.ReadUInt64LittleEndian(bytes),_=>throw new InvalidDataException()};
            if(kind=='f'){double number=header.ItemBytes switch{2=>(double)BitConverter.UInt16BitsToHalf((ushort)bits),4=>BitConverter.Int32BitsToSingle((int)bits),_=>BitConverter.Int64BitsToDouble((long)bits)};values[i]=double.IsFinite(number)?(object)number:number.ToString(CultureInfo.InvariantCulture);}
            else if(kind=='b')values[i]=bits!=0;
            else if(kind=='u')values[i]=header.ItemBytes==8?(object)bits.ToString(CultureInfo.InvariantCulture):bits;
            else{long number=header.ItemBytes switch{1=>(sbyte)bits,2=>(short)bits,4=>(int)bits,_=>(long)bits};values[i]=header.ItemBytes==8?(object)number.ToString(CultureInfo.InvariantCulture):number;}
        }
        return new{schema="npy-numeric-v1",header,sampleValues=values,truncated=header.Elements>count,structureComplete=true,
            note="Values are a bounded sample in stored C/Fortran order. Export original NPY to preserve every array value exactly; no Python/pickle execution."};
    }
    sealed class MemberStream(Stream source,ZipArchive archive,Stream member,long length):Stream
    {
        long position;
        public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>false;
        public override long Length=>length;public override long Position{get=>position;set=>throw new NotSupportedException();}
        public override int Read(byte[] buffer,int offset,int count)=>Read(buffer.AsSpan(offset,count));
        public override int Read(Span<byte> buffer){int n=member.Read(buffer);position+=n;return n;}
        protected override void Dispose(bool disposing){if(disposing){member.Dispose();archive.Dispose();source.Dispose();}base.Dispose(disposing);}
        public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();public override void Flush(){}
        public override void SetLength(long value)=>throw new NotSupportedException();public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
    }
}
