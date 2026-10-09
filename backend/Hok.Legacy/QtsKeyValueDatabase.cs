using System;
using System.IO;
using System.Collections.Generic;
using System.Buffers.Binary;

namespace AssetStudio;

// QTS is also used for uncompressed key/value databases. Their values do not
// start with a VFS decoded-size word. Keep every linked record, including keys
// that occur more than once, instead of silently applying last-write-wins.
public sealed class QtsKeyValueDatabase
{
    public sealed record Record(int Offset,int Next,int RecordBytes,int KeyOffset,int ValueOffset,ReadOnlyMemory<byte> Key,ReadOnlyMemory<byte> Value);
    public List<Record> Records {get;}=new();
    public int Pages {get;private set;}
    public int Bytes {get;private set;}
    public static bool HasSignature(ReadOnlySpan<byte> b)=>b.Length>=80&&b[..8].SequenceEqual(new byte[]{1,0,0,2,1,2,3,4});
    public static QtsKeyValueDatabase Parse(byte[] bytes)
    {
        var db=new QtsKeyValueDatabase{Bytes=bytes.Length};
        void Range(int p,int n){if(p<0||n<0||p>bytes.Length-n)throw new InvalidDataException($"QTS KV range {p}+{n}/{bytes.Length}");}
        int I32(int p){Range(p,4);return BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(p));}
        ushort U16(int p){Range(p,2);return BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(p));}
        if(!HasSignature(bytes))throw new InvalidDataException("QTS KV header signature");
        var tables=new HashSet<int>{I32(48),I32(56),I32(64)};var queue=new List<int>();var seenPages=new HashSet<int>();var records=new List<int>();
        foreach(int t in tables)
        {
            if(t==-1)continue;Range(t,24);int capacity=I32(t),count=I32(t+4);
            if(count<0||count>capacity||count>(bytes.Length-24)/24)throw new InvalidDataException("QTS KV table count");Range(t+24,checked(count*24));
            for(int i=0;i<count;i++)for(int j=0;j<3;j++){int p=I32(t+24+i*24+j*4);if(p>0)queue.Add(p);}
        }
        for(int i=0;i<queue.Count;i++)
        {
            int p=queue[i];if(!seenPages.Add(p))continue;Range(p,4096);int count=U16(p+4092),type=U16(p+4094),next=I32(p+4088);
            if(count>510||type is not (0 or 2 or 3))throw new InvalidDataException("QTS KV page type/count");if(next>0)queue.Add(next);
            for(int j=0;j<count;j++){int at=I32(p+2040+j*4);if(at>0){if(type==2)queue.Add(at);else records.Add(at);}}
        }
        var seenRecords=new HashSet<int>();
        for(int i=0;i<records.Count;i++)
        {
            int p=records[i];if(!seenRecords.Add(p))continue;Range(p,28);if(I32(p)!=p)throw new InvalidDataException("QTS KV record self-offset mismatch");
            int next=I32(p+4),size=I32(p+8),keys=I32(p+20),values=I32(p+24);
            if(next!=-1){Range(next,28);records.Add(next);}
            if(keys<1||keys>4096||values<0||size<28)throw new InvalidDataException("QTS KV key/value size");Range(p,size);Range(p+28,values);
            int keyAt=checked(p+28+values+3)&~3;Range(keyAt,keys);
            if(keyAt+keys>p+size)throw new InvalidDataException("QTS KV key outside record");
            db.Records.Add(new(p,next,size,keyAt,p+28,bytes.AsMemory(keyAt,keys),bytes.AsMemory(p+28,values)));
        }
        var links=new Dictionary<int,int>();foreach(var r in db.Records)links.Add(r.Offset,r.Next);
        var finished=new HashSet<int>();foreach(var r in db.Records)
        {
            int at=r.Offset;var chain=new HashSet<int>();
            while(at!=-1&&!finished.Contains(at)){if(!chain.Add(at))throw new InvalidDataException("QTS KV record-chain cycle");if(!links.TryGetValue(at,out at))throw new InvalidDataException("QTS KV missing linked record");}
            foreach(int offset in chain)finished.Add(offset);
        }
        db.Pages=seenPages.Count;return db;
    }
}
