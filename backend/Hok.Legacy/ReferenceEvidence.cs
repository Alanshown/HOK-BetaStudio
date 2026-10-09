using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace AssetStudio;

public sealed record ReferenceResolution(string Status, string Reason, long PathId, bool IdentityConfirmed,
    [property: JsonIgnore] SerializedFile TargetFile)
{
    public bool CanRead => TargetFile != null && TargetFile.ObjectsDic.ContainsKey(PathId);
    public string TargetIdentity => TargetFile?.fullName;
}

public interface IObjectReference
{
    int FileId { get; }
    long PathId { get; }
    long? SerializedByteOffset { get; }
    long? ObjectByteOffset { get; }
    string ExpectedType { get; }
    ReferenceResolution ResolveEvidence();
}

public partial class AssetsManager
{
    // This is shared by actual PPtr reads, audit and persistent discovery.
    // A same-DB PathID fallback remains usable for compatibility, but is NOT
    // evidence that the external GUID has been identified.
    public ReferenceResolution ResolveReference(SerializedFile owner, int fileId, long pathId)
    {
        ReferenceResolution Result(string status, string reason, SerializedFile target=null, bool confirmed=false)
            => new(status, reason, pathId, confirmed, target);
        if (pathId == 0) return Result("null", "Null PPtr");
        if (fileId < 0 || fileId > owner.m_Externals.Count) return Result("missing", "Invalid external fileID");
        if (fileId == 0)
            return owner.ObjectsDic.ContainsKey(pathId)
                ? Result("confirmed", "Local fileID 0 + PathID", owner, true)
                : owner.m_Objects.Any(o=>o.m_PathID==pathId)
                ? Result("structure-unparsed", "Local object-table identity exists, but no readable object was produced",owner,true)
                : Result("missing", "PathID absent from the source SerializedFile");
        var external=owner.m_Externals[fileId-1];
        var guid=Convert.ToHexString(external.guid.ToByteArray());
        if ((external.pathName??"").Contains("unity_builtin_extra",StringComparison.OrdinalIgnoreCase) ||
            (external.pathName??"").Contains("unity default resources",StringComparison.OrdinalIgnoreCase) ||
            guid is "00000000000000000E00000000000000" or "00000000000000000F00000000000000")
            return Result("builtin-resource", "Unity built-in external identity; not a missing game DB");
        bool guidOnly=string.IsNullOrEmpty(external.pathName)&&string.IsNullOrEmpty(external.fileName);
        if (owner.game.Type.IsHonorOfKings() && guidOnly)
        {
            if(owner.ObjectsDic.ContainsKey(pathId))
                return Result("local-candidate", "GUID-only external slot; local PathID compatibility fallback, GUID unverified",owner);
            EnsureReferenceIndex();
            if(!string.IsNullOrEmpty(owner.originalPath) &&
                sourceObjects.TryGetValue(owner.originalPath,out var objects) && objects.TryGetValue(pathId,out var match))
                return match==null ? Result("conflict","Multiple same-DB objects share the PathID")
                    : Result("local-candidate","Unique PathID within this DB; external GUID identity remains unverified",match);
            return Result("missing","No same-DB target for GUID-only external; cross-DB candidates require independent evidence");
        }
        var target=ResolveExternal(owner,external);
        if(target==null&&owner.game.Type.IsHonorOfKings()&&!string.IsNullOrEmpty(external.pathName)&&
           !external.pathName.StartsWith("archive:/",StringComparison.OrdinalIgnoreCase))
        {
            // HOK external logical paths hash to QTS entry IDs, not physical
            // filenames. The original path + exact entry ID + PathID is evidence
            // unavailable to a basename or global-PathID-only fallback.
            var matches=FindExplicitQtsExternal(external.pathName,pathId);
            if(matches.Length>1)return Result("conflict","Explicit QTS resource-path hash + PathID has multiple loaded targets");
            if(matches.Length==1)return matches[0].ObjectsDic.ContainsKey(pathId)
                ? Result("confirmed","Explicit external logical path hashes to this QTS entry; PathID matches",matches[0],true)
                : Result("structure-unparsed","Explicit QTS path identifies the raw object table, but the object parser failed",matches[0],true);
        }
        if(target==null) return Result("missing","No unambiguous source-scoped external path");
        // Exact paths retained by the external table or PPtr.Set are source
        // identities. A bare filename fallback is weaker and remains a candidate.
        bool exact=string.Equals(external.pathName,target.fullName,StringComparison.OrdinalIgnoreCase);
        if(!exact && !string.IsNullOrEmpty(external.pathName))
        {
            try{exact=string.Equals(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(owner.fullName),external.pathName)),target.fullName,StringComparison.OrdinalIgnoreCase);}
            catch(ArgumentException){}catch(NotSupportedException){}
        }
        if(!target.ObjectsDic.ContainsKey(pathId))return target.m_Objects.Any(o=>o.m_PathID==pathId)
            ? Result("structure-unparsed","External object-table identity exists, but no readable object was produced",target,exact)
            : Result("missing","External file resolved, but PathID is absent");
        return exact ? Result("confirmed","Explicit source-scoped external path + PathID",target,true)
            : Result("local-candidate","Unique scoped filename + PathID; GUID identity unverified",target);
    }
}
