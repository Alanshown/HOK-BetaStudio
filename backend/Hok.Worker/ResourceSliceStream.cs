namespace Hok.Worker;
// An independently owned view. Nested BNK/WEM members retain offsets, never
// duplicate a whole package or materialize unselected audio into byte arrays.
internal sealed class ResourceSliceStream : Stream
{
    readonly Stream source; readonly long start, length; long position;
    public ResourceSliceStream(Stream source, long start, long length)
    {
        if (start < 0 || length < 0 || start > source.Length - length) { source.Dispose(); throw new InvalidDataException("Resource member exceeds its parent"); }
        this.source=source;this.start=start;this.length=length;
    }
    public override bool CanRead=>true; public override bool CanSeek=>true; public override bool CanWrite=>false;
    public override long Length=>length;
    public override long Position {get=>position;set {if(value<0||value>length)throw new IOException("Seek outside resource member");position=value;}}
    public override int Read(byte[] buffer,int offset,int count)=>Read(buffer.AsSpan(offset,count));
    public override int Read(Span<byte> buffer){source.Position=start+position;int count=source.Read(buffer[..(int)Math.Min(buffer.Length,length-position)]);position+=count;return count;}
    public override long Seek(long offset,SeekOrigin origin){Position=checked((origin==SeekOrigin.Begin?0:origin==SeekOrigin.Current?position:length)+offset);return position;}
    protected override void Dispose(bool disposing){if(disposing)source.Dispose();base.Dispose(disposing);}
    public override void Flush(){} public override void SetLength(long value)=>throw new NotSupportedException();public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
}
