using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AssetStudio;

// Roles come from a same-container checksum manifest, never a numeric filename
// or a coincidental record size. IDs remain distinct from Unity external GUIDs.
public static class QtsResourceMetadata
{
    public sealed record ResourceRecord(int Offset,string ResourceKey,string BlobId,string[] PathIds);
    public sealed record ScriptRecord(int Offset,string BlobId,string PathId,string[] ScriptPathIds);
    public sealed record OpaqueRecord(int Offset,string KeyHex,string ValueHex);
    public sealed record ScriptTable(ScriptRecord[] Records,OpaqueRecord[] UninterpretedRecords);
    static ulong U64(ReadOnlySpan<byte> bytes)=>BinaryPrimitives.ReadUInt64LittleEndian(bytes);
    static long I64(ReadOnlySpan<byte> bytes)=>BinaryPrimitives.ReadInt64LittleEndian(bytes);
    public static void ValidateResources(QtsKeyValueDatabase db)
    {
        foreach(var r in db.Records)
            if(r.Key.Length!=8||r.Value.Length<16||r.Value.Length%8!=0)
                throw new InvalidDataException("Resource mapping record length at "+r.Offset);
    }
    public static void ValidateScripts(QtsKeyValueDatabase db)
    {
        foreach(var r in db.Records)
            if(!(r.Key.Length==8&&r.Value.Length==8)&&!(r.Key.Length==16&&r.Value.Length%8==0))
                throw new InvalidDataException("Script dependency record length at "+r.Offset);
    }
    public static ResourceRecord[] Resources(QtsKeyValueDatabase db)
    {
        ValidateResources(db);
        var result=new List<ResourceRecord>();
        foreach(var r in db.Records)
        {
            var ids=new string[r.Value.Length/8-1];
            for(int i=0;i<ids.Length;i++)ids[i]=I64(r.Value.Span[(8+i*8)..]).ToString();
            result.Add(new(r.Offset,U64(r.Key.Span).ToString(),U64(r.Value.Span).ToString(),ids));
        }
        return result.ToArray();
    }
    public static ScriptTable Scripts(QtsKeyValueDatabase db)
    {
        ValidateScripts(db);
        var result=new List<ScriptRecord>();var opaque=new List<OpaqueRecord>();
        foreach(var r in db.Records)
        {
            if(r.Key.Length==8&&r.Value.Length==8)
            {opaque.Add(new(r.Offset,Convert.ToHexString(r.Key.Span),Convert.ToHexString(r.Value.Span)));continue;}
            var ids=new string[r.Value.Length/8];
            for(int i=0;i<ids.Length;i++)ids[i]=I64(r.Value.Span[(i*8)..]).ToString();
            result.Add(new(r.Offset,U64(r.Key.Span).ToString(),I64(r.Key.Span[8..]).ToString(),ids));
        }
        return new(result.ToArray(),opaque.ToArray());
    }
}

public partial class AssetsManager
{
    void IdentifyQtsMappingMetadata(QtsDatabaseRecord db)
    {
        if(!db.Entries.Any(e=>e.Complete&&e.Kind=="QtsKeyValueDatabase"))return;
        var manifests=db.Entries.Where(e=>e.Complete&&e.PayloadBytes<=4096)
            .Select(e=>QtsChecksumManifest.TryParse(e.Read(),out var m)?m:null).Where(m=>m!=null).ToArray();
        if(manifests.Length!=1)return;
        foreach(var entry in db.Entries.Where(e=>e.Complete&&e.Kind=="QtsKeyValueDatabase"))
        {
            var bytes=entry.Read();string crc="0x"+Crc32(bytes).ToString("X8");
            var names=manifests[0].Where(kv=>kv.Value==crc).Select(kv=>kv.Key).ToArray();
            if(names.Length!=1)continue;
            try
            {
                var kv=QtsKeyValueDatabase.Parse(bytes);
                switch(names[0])
                {
                    case "ResEntriesDB.db": QtsResourceMetadata.ValidateResources(kv);entry.Kind="QtsResourceMap";break;
                    case "ResScriptDependenciesDB.db": QtsResourceMetadata.ValidateScripts(kv);entry.Kind="QtsScriptDependencies";break;
                    default:continue;
                }
                entry.MetadataName=names[0];
            }
            catch(Exception e)
            {
                entry.Error="Manifest-identified "+names[0]+": "+e.GetBaseException().Message;
                entry.ParseStatus="parser-failed";Logger.Error(entry.Error);
            }
        }
    }
}
