// Based on code from CUE4Parse by FabianFG
// Licensed under the Apache License, Version 2.0
// https://github.com/FabianFG/CUE4Parse

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace AssetStudio;

public class QtsVFSFile
{
    public Dictionary<ulong, List<FHoKCompressedChunk>> Entries = new();
    public FHoKHeader Header;
    public List<string> Issues = new();
    public List<(int Offset, int Count)> Tables = new();
    public List<FHoKEntryBlock> Blocks = new();

    public QtsVFSFile(FileReader reader)
    {
        reader.Endian = EndianType.LittleEndian;
        reader.Position = 0;
        Header = new FHoKHeader(reader);
        var queue = new Queue<int>();
        var visitedTables = new HashSet<int>();
        foreach (var table in new[] { Header.Entries1, Header.Entries2, Header.Entries3 })
        {
            if (table.Offset == -1 || !visitedTables.Add(table.Offset)) continue;
            try {
                Range(reader, table.Offset, 24);
                reader.Position = table.Offset;
                int capacity=reader.ReadInt32(), count=reader.ReadInt32();
                if (count < 0 || capacity < count || count > (table.Size-24)/24) throw new System.IO.InvalidDataException("Invalid table count/size");
                Range(reader, table.Offset+24, (long)count*24);
                Tables.Add((table.Offset,count)); reader.Position=table.Offset+24;
                for(int i=0;i<count;i++) { var t=new FHoKEntriesTable(reader); foreach(int x in new[]{t.Offset1,t.Offset2,t.Offset3}) if(x>0)queue.Enqueue(x); }
            } catch(Exception e) { Issues.Add($"Table {table.Offset}: {e.Message}"); }
        }
        var visitedBlocks=new HashSet<int>(); var offsets=new HashSet<int>();
        while(queue.TryDequeue(out int x)) {
            if(!visitedBlocks.Add(x))continue;
            try {
                Range(reader,x,4096);reader.Position=x+4080;var block=new FHoKEntryBlock(reader);block.Offset=x;Blocks.Add(block);
                if(block.EntryCount>510)throw new System.IO.InvalidDataException("Invalid block entry count");
                if(block.NextOffset>0)queue.Enqueue(block.NextOffset);
                reader.Position=x+2040;var pointers=reader.ReadInt32Array(block.EntryCount);
                if(block.Type==2){foreach(int p in pointers)if(p>0)queue.Enqueue(p);}
                else if(block.Type is 0 or 3){foreach(int p in pointers)if(p>0)offsets.Add(p);}
                else Issues.Add($"Block {x}: unsupported type {block.Type}, {block.EntryCount} pointers retained in original DB");
            } catch(Exception e){Issues.Add($"Block {x}: {e.Message}");}
        }
        var records=new Queue<int>(offsets.OrderBy(x=>x)); var visited=new HashSet<int>();
        while(records.TryDequeue(out int x)) {
            if(!visited.Add(x))continue;
            try {
                Range(reader,x,32);reader.Position=x;
                if(reader.ReadInt32()!=x)throw new System.IO.InvalidDataException("Record self-offset mismatch");
                int next=reader.ReadInt32();if(next!=-1)records.Enqueue(next);
                reader.Position+=16;int compressed=reader.ReadInt32()-4,decoded=reader.ReadInt32();long data=reader.Position;
                if(compressed<0||decoded<0)throw new System.IO.InvalidDataException("Negative chunk size");
                Range(reader,data,compressed);long trailer=(data+compressed+3)&~3L;Range(reader,trailer,16);reader.Position=trailer;
                ulong id=reader.ReadUInt64();int main=reader.ReadInt32(),sub=reader.ReadInt32();
                if(!Entries.TryGetValue(id,out var chunks))Entries.Add(id,chunks=new());
                chunks.Add(new FHoKCompressedChunk(data,compressed,decoded,main,sub));
            }catch(Exception e){Issues.Add($"Record {x}: {e.Message}");}
        }
    }

    private static void Range(FileReader reader,long offset,long size) { if(offset<0||size<0||offset>reader.Length-size)throw new System.IO.InvalidDataException($"Out of bounds: {offset}+{size}/{reader.Length}"); }

    private static void ReadEntries(FileReader reader, int[] offsets, Dictionary<ulong, List<FHoKCompressedChunk>> result)
    {
        HashSet<int> additionalOffsets = new HashSet<int>();
        foreach (var x in offsets)
        {
            reader.Position = x;
            if (reader.ReadInt32() != x) continue;
            var next = reader.ReadInt32();
            if (next != -1 && !offsets.Contains(next)) additionalOffsets.Add(next);
            reader.Position += 16;
            var compressedSize = reader.ReadInt32() - 4;
            var uncompressedSize = reader.ReadInt32();
            var offset = reader.Position;

            reader.Position += compressedSize;
            reader.AlignStream(4);
            var id = reader.ReadUInt64();
            var mainBlock = reader.ReadInt32();  // Main block sequence number
            var subBlock = reader.ReadInt32();   // Sub block sequence number
            var entry = new FHoKCompressedChunk(offset, compressedSize, uncompressedSize, mainBlock, subBlock);

            if (result.TryGetValue(id, out var list))
            {
                list.Add(entry);
            }
            else
            {
                result[id] = new List<FHoKCompressedChunk> { entry };
            }
        }

        if (additionalOffsets.Count > 0)
            ReadEntries(reader, additionalOffsets.ToArray(), result);
    }


    private static FHoKEntriesTable[] FEntriesTables(FileReader reader, int offset)
    {
        if (offset == -1) return Array.Empty<FHoKEntriesTable>();
        reader.Position = offset;
        var max = reader.ReadInt32();
        var count = reader.ReadInt32();
        reader.Position += 16;
        return reader.ReadArray(() => new FHoKEntriesTable(reader), count);
    }


    // from cue4parse
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct FHoKHeader
    {
        public ulong Magic;
        public int Size;
        public int FullSize;
        public FHoKEntriesOffsetSize Unknown0;
        public FHoKEntriesOffsetSize Unknown1; // always -1
        public FHoKEntriesOffsetSize Index;
        public FHoKEntriesOffsetSize IndexData;
        public FHoKEntriesOffsetSize Entries1;
        public FHoKEntriesOffsetSize Entries2;
        public FHoKEntriesOffsetSize Entries3;
        public FHoKEntriesOffsetSize Unknown2; // always -1

        public FHoKHeader(FileReader reader)
        {
            Magic = reader.ReadUInt64();
            Size = reader.ReadInt32();
            FullSize = reader.ReadInt32();
            Unknown0 = new FHoKEntriesOffsetSize(reader);
            Unknown1 = new FHoKEntriesOffsetSize(reader);
            Index = new FHoKEntriesOffsetSize(reader);
            IndexData = new FHoKEntriesOffsetSize(reader);
            Entries1 = new FHoKEntriesOffsetSize(reader);
            Entries2 = new FHoKEntriesOffsetSize(reader);
            Entries3 = new FHoKEntriesOffsetSize(reader);
            Unknown2 = new FHoKEntriesOffsetSize(reader);
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct FHoKEntriesTable
    {
        public int Offset1;
        public int Offset2;
        public int Offset3;
        public int Unknown1;
        public int Unknown2;
        public int Unknown3;

        public FHoKEntriesTable(FileReader reader)
        {
            Offset1 = reader.ReadInt32();
            Offset2 = reader.ReadInt32();
            Offset3 = reader.ReadInt32();
            Unknown1 = reader.ReadInt32();
            Unknown2 = reader.ReadInt32();
            Unknown3 = reader.ReadInt32();
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct FHoKEntriesOffsetSize
    {
        public int Offset;
        public int Size;

        public FHoKEntriesOffsetSize(FileReader reader)
        {
            Offset = reader.ReadInt32();
            Size = reader.ReadInt32();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FHoKEntryBlock
    {
        public int Offset;
        public int Unknown;
        public int NextOffset;
        public ushort EntryCount;
        public ushort Type;

        public FHoKEntryBlock(FileReader reader)
        {
            Offset = reader.ReadInt32();
            Unknown = reader.ReadInt32();
            NextOffset = reader.ReadInt32();
            EntryCount = reader.ReadUInt16();
            Type = reader.ReadUInt16();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct FHoKCompressedChunk
    {
        public readonly long Offset;
        public readonly int CompressedSize;
        public readonly int UncompressedSize;
        public readonly int MainBlock;  // Main block sequence number, used for sorting
        public readonly int SubBlock;   // Sub block sequence number, used for sorting

        public FHoKCompressedChunk(long offset, int compressedSize, int uncompressedSize, int mainBlock, int subBlock)
        {
            Offset = offset;
            CompressedSize = compressedSize;
            UncompressedSize = uncompressedSize;
            MainBlock = mainBlock;
            SubBlock = subBlock;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static ulong Compute(string text, bool addSlash)
    {
        ArgumentNullException.ThrowIfNull(text);

        int length = text.Length;
        Span<char> raw = length <= 256 ? stackalloc char[length + 1] : new char[length + 1];
        if (addSlash)
        {
            raw[0] = '/';
            text.AsSpan().ToLowerInvariant(raw[1..]);
            length++;
        }
        else
        {
            text.AsSpan().ToLowerInvariant(raw);
        }

        uint h1 = 0x5BD1E995;
        uint h2 = 0xAB9423A7;

        if (length == 0)
            return ((ulong)h2 << 32) | h1;

        int left = 0;
        int right = length - 1;

        while (left < length)
        {
            h1 = (h1 * 33u) ^ raw[left++];
            h2 = (h2 * 33u) ^ raw[right--];
        }

        return ((ulong)h2 << 32) | h1;
    }
}
