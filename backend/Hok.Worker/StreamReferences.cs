using AssetStudio;
using System.Text.Json;
using Obj = AssetStudio.Object;

namespace Hok.Worker;
internal static partial class Program
{
    // Paged, explicit audit: decoding deferred meshes does not retain the whole
    // corpus in RAM. Metadata from separately tested DBs can be joined by the
    // reference hash without claiming that a random byte signature is a texture.
    static object StreamReferences(JsonElement p)
    {
        int page = p.TryGetProperty("page", out var n) ? Math.Max(0, n.GetInt32()) : 0;
        var candidates = Rows.Where(r => r.ClassId is 28 or 43 or 83 or 89).ToList();
        var rows = new List<object>();
        foreach (var row in candidates.Skip(checked(page * 100)).Take(100))
        {
            try
            {
                var obj = DeferredObject.Resolve(Objects[row.Id]);
                var stream = GetStream(obj);
                if (stream is not { } s) { rows.Add(new { row.Id, row.Type, row.Source, hasStream = false, asset = RefreshRow(row) }); continue; }
                ulong hash = QtsVFSFile.Compute(s.Path, true);
                IdentifyStream(obj, s.Path, s.Kind);
                rows.Add(new { row.Id, row.Type, row.Source, hasStream = true, streamPath = s.Path, fileId = hash.ToString(), offset = s.Offset, size = s.Size, kind = s.Kind, asset = RefreshRow(row) });
            }
            catch (Exception e) { rows.Add(new { row.Id, row.Type, row.Source, error = e.GetBaseException().Message, asset = RefreshRow(row) }); }
        }
        return new { total = candidates.Count, page, items = rows };
    }

    static (string Path, long Offset, long Size, string Kind)? GetStream(Obj obj) => obj switch
    {
        Cubemap t when t.m_StreamData is { path.Length: > 0 } s => (s.path, s.offset, s.size, "CubemapStream/" + t.m_TextureFormat),
        Texture2D t when t.m_StreamData is { path.Length: > 0 } s => (s.path, s.offset, s.size, "TextureStream/" + t.m_TextureFormat),
        Mesh m when m.StreamingData is { path.Length: > 0 } s => (s.path, s.offset, s.size, "MeshStream"),
        AudioClip a when !string.IsNullOrEmpty(a.m_Source) => (a.m_Source, (long)a.m_Offset, (long)a.m_Size, "AudioStream/" + a.m_CompressionFormat),
        _ => null
    };

    static void IdentifyStream(Obj obj, string path, string kind)
    {
        if (manager is null || !manager.GlobalQtsVFSIndex.TryGetValue(QtsVFSFile.Compute(path, true), out var matches)) return;
        var usable = matches.Where(e => e.Complete && e.Kind is not ("QtsIndexRecord" or "QtsPackageMetadata")).ToArray();
        var same = usable.Where(e => string.Equals(e.Source, obj.assetsFile.originalPath, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (same.Length > 0) usable = same;
        if (usable.Length == 1) { usable[0].Kind = kind; usable[0].ParseStatus = "identified-by-reference"; }
    }
}
