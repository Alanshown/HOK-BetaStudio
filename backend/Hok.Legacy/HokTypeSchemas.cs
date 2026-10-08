using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Text.Json;
namespace AssetStudio;

// Engine layouts are not guessed by object length. Only the exact engine
// version + class + verified type-hash profiles are eligible, and every object
// must still pass a complete bounded read before it becomes structured data.
internal static class HokTypeSchemas
{
    sealed record Profile(HashSet<string> Hashes, TypeTree Tree);
    static readonly Dictionary<int,Profile> Profiles=Load();
    static Dictionary<int,Profile> Load(){
        var result=new Dictionary<int,Profile>();
        using var resource=typeof(HokTypeSchemas).Assembly.GetManifestResourceStream("Hok.Schemas.2022.3.5f1");
        if(resource==null)return result;
        using var text=new StreamReader(resource);
        using var compressed=new MemoryStream(Convert.FromBase64String(text.ReadToEnd()));
        using var gzip=new GZipStream(compressed,CompressionMode.Decompress);
        using var json=JsonDocument.Parse(gzip);
        Add(json);
        using var extraResource=typeof(HokTypeSchemas).Assembly.GetManifestResourceStream("Hok.Schemas.Extra.2022.3.5f1");
        if(extraResource!=null){using var extra=JsonDocument.Parse(extraResource);Add(extra);}
        return result;
        void Add(JsonDocument document){foreach(var c in document.RootElement.GetProperty("classes").EnumerateObject()){
            var hashes=new HashSet<string>();foreach(var h in c.Value.GetProperty("hashes").EnumerateArray())hashes.Add(h.GetString());
            var tree=new TypeTree{m_Nodes=new List<TypeTreeNode>()};
            foreach(var n in c.Value.GetProperty("nodes").EnumerateArray())tree.m_Nodes.Add(new TypeTreeNode{m_Type=n[0].GetString(),m_Name=n[1].GetString(),m_Level=n[2].GetInt32(),m_ByteSize=n[3].GetInt32(),m_TypeFlags=n[4].GetInt32(),m_MetaFlag=n[5].GetInt32(),m_Version=n[6].GetInt32()});
            result.Add(int.Parse(c.Name),new Profile(hashes,tree));
        }}
    }
    public static TypeTree Find(ObjectReader reader){
        if(!reader.Game.Type.IsHonorOfKings()||reader.assetsFile.unityVersion!="2022.3.5f1")return null;
        var type=reader.serializedType;
        return type?.m_OldTypeHash!=null&&Profiles.TryGetValue((int)reader.type,out var p)&&p.Hashes.Contains(Convert.ToHexString(type.m_OldTypeHash))?p.Tree:null;
    }
}
