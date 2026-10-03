using System.Buffers.Binary;
using AssetStudio;
using ZstdSharp;

namespace Hok.Rebuild;

public sealed record SlotPatch(long Offset, byte[] Encoded, byte[] Decoded);

/// <summary>
/// Experimental fixed-slot encoder. Index records and all unmodified bytes remain
/// untouched. Unknown QTS digest fields cannot currently be recomputed; therefore
/// successful decoding is NOT evidence of game compatibility.
/// </summary>
public static class ExperimentalSlotWriter
{
    public const string Warning = "Beta experimental / not recommended. Fixed-length raw binary only. Modified blocks use Zstandard with skippable padding; original codec may change. Unknown QTS digest/metadata fields and record.bytes are preserved, not recomputed. Game compatibility is unverified.";

    public static SlotPatch[] Prepare(string db, ulong fileId, byte[] original, byte[] replacement)
    {
        if (original.Length != replacement.Length) throw new InvalidDataException("Beta replacement must have exactly the original decoded byte length.");
        using var reader = new FileReader(db);
        var qts = new QtsVFSFile(reader);
        if (!qts.Entries.TryGetValue(fileId, out var records)) throw new InvalidDataException("QTS file ID not present.");
        var chunks = records.Where(c => c.UncompressedSize > 0).OrderBy(c => c.MainBlock).ThenBy(c => c.SubBlock).ToArray();
        if (chunks.Sum(c => (long)c.UncompressedSize) != original.LongLength) throw new InvalidDataException("QTS chunk layout differs from the decoded source.");
        int cursor = 0;
        var patches = new List<SlotPatch>();
        foreach (var chunk in chunks)
        {
            var data = replacement.AsSpan(cursor, chunk.UncompressedSize);
            if (!data.SequenceEqual(original.AsSpan(cursor, chunk.UncompressedSize)))
            {
                byte[]? encoded = null;
                foreach (int level in new[] { 3, 9, 15, 19 })
                {
                    using var compressor = new Compressor(level);
                    var frame = compressor.Wrap(data).ToArray();
                    int remaining = chunk.CompressedSize - frame.Length;
                    if (remaining < 0 || remaining is > 0 and < 8) continue;
                    encoded = new byte[chunk.CompressedSize];
                    frame.CopyTo(encoded, 0);
                    if (remaining > 0)
                    {
                        // Standard Zstandard skippable frame, not arbitrary trailing garbage.
                        // https://github.com/facebook/zstd/blob/dev/doc/zstd_compression_format.md#skippable-frames
                        BinaryPrimitives.WriteUInt32LittleEndian(encoded.AsSpan(frame.Length), 0x184D2A50);
                        BinaryPrimitives.WriteUInt32LittleEndian(encoded.AsSpan(frame.Length + 4), (uint)(remaining - 8));
                    }
                    using var decompressor = new Decompressor();
                    var decoded = new byte[chunk.UncompressedSize];
                    int written = decompressor.Unwrap(encoded, 0, encoded.Length, decoded, 0, decoded.Length);
                    if (written != data.Length || !data.SequenceEqual(decoded)) throw new InvalidDataException("Encoded QTS block did not round-trip exactly.");
                    break;
                }
                if (encoded is null) throw new InvalidDataException("Replacement cannot fit the original compressed slot. Beta does not relocate or enlarge QTS blocks.");
                if (chunk.Offset < 0 || chunk.Offset > reader.BaseStream.Length - encoded.Length) throw new InvalidDataException("Invalid QTS slot bounds.");
                patches.Add(new(chunk.Offset, encoded, data.ToArray()));
            }
            cursor += chunk.UncompressedSize;
        }
        return patches.ToArray();
    }

    public static void ApplyToNewCopy(string copy, IEnumerable<SlotPatch> patches)
    {
        var sorted = patches.OrderBy(p => p.Offset).ToArray();
        using var file = new FileStream(copy, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        long end = 0;
        foreach (var patch in sorted)
        {
            if (patch.Offset < end || patch.Offset > file.Length - patch.Encoded.Length) throw new InvalidDataException("Overlapping or invalid patch slots.");
            file.Position = patch.Offset; file.Write(patch.Encoded); end = patch.Offset + patch.Encoded.Length;
        }
        file.Flush(true);
    }
}
