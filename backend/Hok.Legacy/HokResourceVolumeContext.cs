using System;
using System.Collections.Generic;
using System.IO;

namespace AssetStudio;

// Observed HOK class 90001. This is a resource-volume table, NOT a skin/battle
// manifest. Only the independently bounded prefix is interpreted; the trailing
// platform fields stay partial rather than being consumed as unnamed padding.
public sealed class HokResourceVolumeContext : NamedObject
{
    public List<PPtr<Object>> ReferencedObjects = new();
    public List<TypeIdentity> ClassTypeHashes = new();
    public bool EmptyExtensionTable;
    public sealed class TypeIdentity { public int ClassId; public string Hash; public long Offset; }
    public static bool Supports(ObjectReader reader)=>reader.Game.Type.IsHonorOfKings()&&reader.assetsFile.unityVersion=="2022.3.5f1"&&
        reader.serializedType?.m_OldTypeHash is {} hash&&Convert.ToHexString(hash)=="AE6C172B69C68B22157E6F01D63362D8";
    public HokResourceVolumeContext(ObjectReader reader):base(reader)
    {
        int count=Count(reader,12);for(int i=0;i<count;i++)ReferencedObjects.Add(new PPtr<Object>(reader));
        long at=reader.Position;int extensionCount=reader.ReadInt32();
        if(extensionCount!=0){reader.Position=at;return;}
        EmptyExtensionTable=true;
        count=Count(reader,20);
        for(int i=0;i<count;i++)ClassTypeHashes.Add(new(){Offset=reader.Position,ClassId=reader.ReadInt32(),Hash=Convert.ToHexString(reader.ReadBytes(16))});
    }
    static int Count(ObjectReader reader,int stride)
    {
        int count=reader.ReadInt32();if(count<0||count>(reader.byteStart+reader.byteSize-reader.Position)/stride)throw new InvalidDataException("Resource-volume table exceeds its object boundary");return count;
    }
}
