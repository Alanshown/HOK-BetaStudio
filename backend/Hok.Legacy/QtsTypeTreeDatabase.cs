using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace AssetStudio;

public sealed class QtsTypeTreeDatabase
{
    public sealed record Schema(string Hash,TypeTree Tree,string PayloadSha256,int RecordOffset);
    public List<Schema> Schemas {get;}=new();
    public static TypeTree ReadTree(ReadOnlySpan<byte> bytes)
    {
        if(bytes.Length<8)throw new InvalidDataException("Type tree header");
        int count=BinaryPrimitives.ReadInt32LittleEndian(bytes),strings=BinaryPrimitives.ReadInt32LittleEndian(bytes[4..]);
        if(count<1||count>100000||strings<0||8L+count*32L+strings!=bytes.Length)throw new InvalidDataException("Type tree length/count");
        var stringData=bytes[(8+count*32)..].ToArray();var utf8=new UTF8Encoding(false,true);
        string Text(uint offset)
        {
            if((offset&0x80000000)!=0)return CommonString.StringBuffer.TryGetValue(offset&0x7fffffff,out var value)?value:throw new InvalidDataException("Unknown type tree common string");
            if(offset>=stringData.Length)throw new InvalidDataException("Type tree string offset");
            int end=Array.IndexOf(stringData,(byte)0,(int)offset);if(end<0)throw new InvalidDataException("Unterminated type tree string");
            return utf8.GetString(stringData,(int)offset,end-(int)offset);
        }
        var tree=new TypeTree{m_Nodes=new List<TypeTreeNode>(),m_StringBuffer=stringData};int previous=0;
        for(int i=0;i<count;i++)
        {
            var n=bytes.Slice(8+i*32,32);int level=n[2];
            if(i==0&&level!=0||i>0&&(level==0||level>previous+1))throw new InvalidDataException("Type tree hierarchy");previous=level;
            tree.m_Nodes.Add(new TypeTreeNode{m_Version=BinaryPrimitives.ReadUInt16LittleEndian(n),m_Level=level,m_TypeFlags=n[3],
                m_TypeStrOffset=BinaryPrimitives.ReadUInt32LittleEndian(n[4..]),m_NameStrOffset=BinaryPrimitives.ReadUInt32LittleEndian(n[8..]),
                m_Type=Text(BinaryPrimitives.ReadUInt32LittleEndian(n[4..])),m_Name=Text(BinaryPrimitives.ReadUInt32LittleEndian(n[8..])),
                m_ByteSize=BinaryPrimitives.ReadInt32LittleEndian(n[12..]),m_Index=BinaryPrimitives.ReadInt32LittleEndian(n[16..]),
                m_MetaFlag=BinaryPrimitives.ReadInt32LittleEndian(n[20..]),m_RefTypeHash=BinaryPrimitives.ReadUInt64LittleEndian(n[24..])});
        }
        return tree;
    }
    public static bool LooksLike(QtsKeyValueDatabase db)=>db.Records.Count>0&&db.Records.All(r=>r.Key.Length==16)&&db.Records[0].Value.Length>=8&&BinaryPrimitives.ReadInt32LittleEndian(db.Records[0].Value.Span)>0&&8L+32L*BinaryPrimitives.ReadInt32LittleEndian(db.Records[0].Value.Span)+BinaryPrimitives.ReadInt32LittleEndian(db.Records[0].Value.Span[4..])==db.Records[0].Value.Length;
    public static QtsTypeTreeDatabase Parse(QtsKeyValueDatabase db)
    {
        var result=new QtsTypeTreeDatabase();
        foreach(var r in db.Records)
        {
            if(r.Key.Length!=16)throw new InvalidDataException("Type tree key must be a complete 16-byte type hash");
            result.Schemas.Add(new(Convert.ToHexString(r.Key.Span),ReadTree(r.Value.Span),Convert.ToHexString(SHA256.HashData(r.Value.Span)),r.Offset));
        }
        return result;
    }
}

public partial class AssetsManager
{
    readonly Dictionary<string,List<(QtsTypeTreeDatabase.Schema Schema,string Source)>> qtsTypeSchemas=new(StringComparer.Ordinal);
    bool qtsTypeDependenciesChecked;
    public object[] QtsTypeSchemaSources=>qtsTypeSchemas.Values.SelectMany(x=>x).GroupBy(x=>x.Source).Select(g=>(object)new{source=g.Key,schemas=g.Count()}).ToArray();
    static uint Crc32(ReadOnlySpan<byte> bytes){uint crc=uint.MaxValue;foreach(byte b in bytes){crc^=b;for(int i=0;i<8;i++)crc=(crc>>1)^((crc&1)!=0?0xedb88320u:0);}return ~crc;}
    void EnsureQtsTypeDependencies()
    {
        if(qtsTypeDependenciesChecked)return;qtsTypeDependenciesChecked=true;
        // A known corpus lookup accelerator, NOT an identity assertion. The
        // same-source manifest CRC, full KV/tree validation and per-object
        // type hash all have to agree before a schema can be used. Only paths
        // supplied by the workspace are searched; no external game folders.
        const ulong treeId=18078322517950175870UL,manifestId=11634658883179902350UL;
        if(!GlobalQtsVFSIndex.ContainsKey(treeId))IndexQtsDependencies(treeId);
        if(!GlobalQtsVFSIndex.TryGetValue(treeId,out var candidates))return;
        if(!GlobalQtsVFSIndex.ContainsKey(manifestId))IndexQtsDependencies(manifestId);
        if(!GlobalQtsVFSIndex.TryGetValue(manifestId,out var manifests))return;
        foreach(var candidate in candidates)
        {
            var manifest=manifests.Where(m=>m.Source==candidate.Source).ToArray();if(manifest.Length!=1)continue;
            try
            {
                if(!manifest[0].Complete)manifest[0].Recover?.Invoke();if(!manifest[0].Complete||!QtsChecksumManifest.TryParse(manifest[0].Read(),out var checksums)||!checksums.TryGetValue("TTre.db",out var expected))continue;
                if(!candidate.Complete)candidate.Recover?.Invoke();if(!candidate.Complete)throw new InvalidDataException(candidate.Error);
                var bytes=candidate.Read();if("0x"+Crc32(bytes).ToString("X8")!=expected)throw new InvalidDataException("TTre.db manifest CRC mismatch");
                RegisterQtsTypeDatabase(QtsTypeTreeDatabase.Parse(QtsKeyValueDatabase.Parse(bytes)),candidate.Source+".entries/"+treeId);
            }
            catch(Exception e){QtsReferenceDiagnostics.Add("QTS schema dependency "+candidate.Source+": "+e.Message);Logger.Warning("QTS schema dependency: "+e.Message);}
        }
    }
    public void LoadQtsTypeDatabase(string path)=>RegisterQtsTypeDatabase(QtsTypeTreeDatabase.Parse(QtsKeyValueDatabase.Parse(File.ReadAllBytes(path))),Path.GetFullPath(path));
    void RegisterQtsTypeDatabase(QtsTypeTreeDatabase db,string source)
    {
        foreach(var schema in db.Schemas)
        {
            if(!qtsTypeSchemas.TryGetValue(schema.Hash,out var list))qtsTypeSchemas.Add(schema.Hash,list=new());
            if(!list.Any(x=>x.Source==source&&x.Schema.PayloadSha256==schema.PayloadSha256))list.Add((schema,source));
        }
    }
    internal TypeTree FindQtsTypeSchema(ObjectReader reader)
    {
        if(!reader.Game.Type.IsHonorOfKings())return null;
        var tree=FindQtsTypeSchema(reader.serializedType);
        if(tree!=null&&Enum.IsDefined(typeof(ClassIDType),reader.type)&&tree.m_Nodes[0].m_Type!=ObjectParseStatus.ClassName((int)reader.type))return null;
        return tree;
    }
    internal TypeTree FindQtsTypeSchema(SerializedType type)
    {
        if(type?.m_OldTypeHash is not{} hash)return null;
        if(!qtsTypeSchemas.ContainsKey(Convert.ToHexString(hash)))EnsureQtsTypeDependencies();
        if(!qtsTypeSchemas.TryGetValue(Convert.ToHexString(hash),out var candidates))return null;
        if(candidates.Select(c=>c.Schema.PayloadSha256).Distinct().Count()!=1)throw new InvalidDataException("Conflicting QTS type trees for "+Convert.ToHexString(hash));
        return candidates[0].Schema.Tree;
    }
}
