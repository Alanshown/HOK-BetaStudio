using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AssetStudio;

// INGQ routes QTS resource IDs, never Unity GUIDs or PathIDs. All counts,
// sorting, sentinel collision groups and package indices are validated.
public sealed class QtsRoutingCatalog
{
    public const int Magic=0x51474e49;
    public sealed record Package(string Name,string Opaque8Hex,int Offset);
    public sealed record Route(string ResourceId,string PackageName,int PackageIndex,string Evidence);
    public Package[] Packages {get;private set;}
    public uint[] Low32Keys {get;private set;}
    public ushort[] PackageIndices {get;private set;}
    public ulong[] CollisionKeys {get;private set;}
    public ushort[] CollisionPackages {get;private set;}
    public int Bytes {get;private set;}
    public byte[] Header {get;private set;}
    public static QtsRoutingCatalog Parse(byte[] raw)
    {
        int p=0;void Need(int n){if(n<0||p>raw.Length-n)throw new InvalidDataException("INGQ range at "+p);}
        uint U32(){Need(4);uint n=BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(p));p+=4;return n;}
        int Count(int expected,int stride){int count=checked((int)U32());if(count!=expected||count>(raw.Length-p)/stride)throw new InvalidDataException("INGQ array count at "+(p-4));return count;}
        if(U32()!=Magic||U32()!=2||raw.Length<56)throw new InvalidDataException("Unsupported INGQ catalog header");
        p=32;int packageCount=checked((int)U32()),resourceCount=checked((int)U32()),collisionCount=checked((int)U32());
        if(packageCount<=0||packageCount>65535||resourceCount<0||collisionCount<0||packageCount>(raw.Length-56)/16)throw new InvalidDataException("INGQ header counts");
        var result=new QtsRoutingCatalog{Bytes=raw.Length,Header=raw[..56],Packages=new Package[packageCount]};p=56;
        var names=new HashSet<string>(StringComparer.Ordinal);
        for(int i=0;i<packageCount;i++)
        {
            int at=p,n=checked((int)U32());if(n<1||n>128)throw new InvalidDataException("INGQ package name length");Need(n);
            for(int j=0;j<n;j++)if(raw[p+j]<'0'||raw[p+j]>'9')throw new InvalidDataException("Non-numeric INGQ package name");
            string name=Encoding.ASCII.GetString(raw,p,n);p+=n;if(!names.Add(name))throw new InvalidDataException("Duplicate INGQ package name");
            while((p&7)!=0){Need(1);if(raw[p++]!=0)throw new InvalidDataException("INGQ package padding");}
            Need(8);result.Packages[i]=new(name,Convert.ToHexString(raw.AsSpan(p,8)),at);p+=8;
        }
        Count(resourceCount,4);result.Low32Keys=new uint[resourceCount];
        for(int i=0;i<resourceCount;i++){uint value=U32();if(i>0&&value<=result.Low32Keys[i-1])throw new InvalidDataException("INGQ low32 keys not strictly sorted");result.Low32Keys[i]=value;}
        Count(resourceCount,2);result.PackageIndices=new ushort[resourceCount];var sentinels=new HashSet<uint>();
        for(int i=0;i<resourceCount;i++)
        {ushort value=BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(p));p+=2;if(value!=65535&&value>=packageCount)throw new InvalidDataException("INGQ package index out of bounds");if(value==65535)sentinels.Add(result.Low32Keys[i]);result.PackageIndices[i]=value;}
        Count(collisionCount,8);result.CollisionKeys=new ulong[collisionCount];var seenSentinels=new HashSet<uint>();
        for(int i=0;i<collisionCount;i++)
        {ulong value=BinaryPrimitives.ReadUInt64LittleEndian(raw.AsSpan(p));p+=8;if(i>0&&value<=result.CollisionKeys[i-1])throw new InvalidDataException("INGQ collision keys not strictly sorted");if(!sentinels.Contains((uint)value))throw new InvalidDataException("INGQ collision missing low32 sentinel");seenSentinels.Add((uint)value);result.CollisionKeys[i]=value;}
        Count(collisionCount,2);result.CollisionPackages=new ushort[collisionCount];
        for(int i=0;i<collisionCount;i++){ushort value=BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(p));p+=2;if(value>=packageCount)throw new InvalidDataException("INGQ collision package out of bounds");result.CollisionPackages[i]=value;}
        if(p!=raw.Length||!sentinels.SetEquals(seenSentinels))throw new InvalidDataException("INGQ trailing bytes or incomplete collision table");
        return result;
    }
    public Route Resolve(ulong qtsResourceId)
    {
        int index=Array.BinarySearch(Low32Keys,(uint)qtsResourceId);if(index<0)return null;
        int package=PackageIndices[index];string evidence="QTS low32 routing key";
        if(package==65535){int collision=Array.BinarySearch(CollisionKeys,qtsResourceId);if(collision<0)return null;package=CollisionPackages[collision];evidence="QTS full64 collision table";}
        return new(qtsResourceId.ToString(),Packages[package].Name,package,evidence);
    }
    public object Summary()=>new{schema="ingq-v2",bytes=Bytes,packages=Packages,resourceCount=Low32Keys.Length,collisionCount=CollisionKeys.Length,
        originalHeaderHex=Convert.ToHexString(Header),structureComplete=true,semanticComplete=false,
        identityDomain="QTS resource IDs only; not Unity GUIDs or PathIDs",unknownFields="header and per-package opaque eight bytes retained without inferred meanings"};
}
