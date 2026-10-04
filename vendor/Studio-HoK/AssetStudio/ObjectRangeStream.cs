using System;
using System.IO;

namespace AssetStudio
{
    // Keep absolute SerializedFile offsets, but bound all BinaryReader primitive
    // and block reads to one object. The underlying file remains owned by manager.
    internal sealed class ObjectRangeStream : Stream
    {
        private readonly Stream source;
        private readonly long start, end;
        public ObjectRangeStream(Stream source, long start, long size)
        {
            if (start < 0 || size < 0 || start > source.Length - size)
                throw new InvalidDataException($"Object range outside SerializedFile: {start}+{size}/{source.Length}");
            this.source = source; this.start = start; end = start + size;
            source.Position = start;
        }
        public override bool CanRead => source.CanRead;
        public override bool CanSeek => source.CanSeek;
        public override bool CanWrite => false;
        public override long Length => end;
        public override long Position
        {
            get => source.Position;
            set { if (value < start || value > end) throw new EndOfStreamException("Seek outside object range"); source.Position = value; }
        }
        private int Available(int count)
        {
            if (Position < start || Position > end) throw new EndOfStreamException("Read outside object range");
            return (int)Math.Min(count, end - Position);
        }
        public override int Read(byte[] buffer, int offset, int count) => source.Read(buffer, offset, Available(count));
        public override int Read(Span<byte> buffer) => source.Read(buffer[..Available(buffer.Length)]);
        public override int ReadByte() => Available(1) == 0 ? -1 : source.ReadByte();
        public override long Seek(long offset, SeekOrigin origin)
        {
            Position = origin switch { SeekOrigin.Begin => offset, SeekOrigin.Current => checked(Position + offset), SeekOrigin.End => checked(end + offset), _ => throw new ArgumentOutOfRangeException(nameof(origin)) };
            return Position;
        }
        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
