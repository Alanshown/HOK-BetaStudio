using System;
using System.IO;
using System.Buffers;
using System.Runtime.InteropServices;
using AssetStudio.PInvoke;

namespace AssetStudio;
public static class OozHelper
{
    static OozHelper()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            DllLoader.PreloadDll("ooz");
        }
    }

    [DllImport(@"ooz")]
    static extern int Ooz_Decompress(ref byte compressedBuffer, int compressedBufferSize, ref byte decompressedBuffer, int decompressedBufferSize, int fuzzSafe, int checkCRC, int verbosity, IntPtr rawBuffer, int rawBufferSize, IntPtr fpCallback, IntPtr callbackUserData, IntPtr decoderMemory, IntPtr decoderMemorySize, int threadPhase);

    public static int Decompress(Span<byte> compressed, Span<byte> decompressed)
    {
        if (compressed.Length == 0 || decompressed.Length == 0) throw new InvalidDataException("Empty Oodle block");
        // ooz requires 64 writable bytes beyond logical output (SAFE_SPACE).
        // Keep vector writes away from adjacent managed objects. Codec lengths
        // are unchanged: the padding must never become recovered asset data.
        // https://github.com/powzix/ooz/blob/master/kraken.cpp
        var source = ArrayPool<byte>.Shared.Rent(checked(compressed.Length + 64));
        var target = ArrayPool<byte>.Shared.Rent(checked(decompressed.Length + 64));
        int numWrite = -1;
        try
        {
            compressed.CopyTo(source);source.AsSpan(compressed.Length, 64).Clear();target.AsSpan(decompressed.Length, 64).Clear();
            numWrite = Ooz_Decompress(ref source[0], compressed.Length, ref target[0], decompressed.Length, 1, 0, 0, 0, 0, 0, 0, 0, 0, 3);
            if (numWrite < 0 || numWrite > decompressed.Length) throw new InvalidDataException("Invalid Oodle output length: " + numWrite);
            target.AsSpan(0, numWrite).CopyTo(decompressed);
        }
        catch (Exception e)
        {
            throw new IOException($"Oodle decompression error, write {numWrite} bytes but expected {decompressed.Length} bytes", e);
        }

        finally
        {
            ArrayPool<byte>.Shared.Return(source);
            ArrayPool<byte>.Shared.Return(target);
        }
        return numWrite;
    }
}
