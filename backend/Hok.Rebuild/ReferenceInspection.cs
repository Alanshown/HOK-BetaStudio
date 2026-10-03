using AssetStudio;
using System.Collections;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Obj = AssetStudio.Object;

namespace Hok.Rebuild;

public sealed record ReferenceLink(string File, string Owner, string Field, int FileId, string PathId, string? Guid, string? Path, string Status);
public sealed record Inspection(string StructuralSha256, string ReferenceSchemaSha256, int DbFiles, int ContainerEntries, int SerializedFiles, int Objects,
    int MissingTypeTrees, int GenericObjects, int ResolvedReferences, int UnresolvedReferences, int TextureStreamsMatched,
    Dictionary<string, int> Codecs, ReferenceLink[] References, string[] Warnings);

public static class ReferenceInspection
{
    public static Inspection Inspect(string folder)
    {
        var manifest = PackageBaseline.Capture(folder);
        var paths = manifest.Files.Select(f => Path.Combine(folder, f.RelativePath)).Where(IsQts).Order(StringComparer.Ordinal).ToArray();
        if (paths.Length == 0) throw new InvalidDataException("No QTS DB files in this folder.");
        var codecs = new Dictionary<string, int>();
        var layout = new List<object>();
        foreach (var path in paths)
        {
            using var reader = new FileReader(path);
            var qts = new QtsVFSFile(reader);
            layout.Add(new { file = Path.GetRelativePath(folder, path), entries = qts.Entries.OrderBy(e => e.Key).Select(e => new {
                id = e.Key.ToString(), chunks = e.Value.Select(c => new { c.Offset, c.CompressedSize, c.UncompressedSize, c.MainBlock, c.SubBlock }).ToArray()
            }).ToArray() });
            foreach (var chunk in qts.Entries.Values.SelectMany(c => c).Where(c => c.UncompressedSize > 0))
            {
                reader.Position = chunk.Offset;
                var bytes = reader.ReadBytes(Math.Min(12, chunk.CompressedSize));
                string codec = bytes.AsSpan().StartsWith("QTSF_PACKAGE"u8) ? "verbatim-package"
                    : bytes.AsSpan().StartsWith(new byte[] { 0x28, 0xb5, 0x2f, 0xfd }) ? "zstd"
                    : bytes.Length >= 2 && bytes[0] is 0x8c or 0xcc && bytes[1] == 0x0c ? "oodle" : "lz4";
                codecs[codec] = codecs.GetValueOrDefault(codec) + 1;
            }
        }
        var log = new InspectionLog();
        var previous = Logger.Default;
        Logger.Default = log;
        var manager = new AssetsManager { Game = GameManager.GetGame(GameType.HonorOfKings) };
        try
        {
            manager.LoadFilesReadOnly(paths);
            if (log.Errors.Count != 0) throw new InvalidDataException("Input has parser errors: " + string.Join("; ", log.Errors.Take(5)));
            var links = new List<ReferenceLink>();
            var objects = manager.assetsFileList.SelectMany(f => f.Objects).ToArray();
            foreach (var obj in objects) Walk(obj, obj, "", new HashSet<object>(ReferenceEqualityComparer.Instance), 0, links, manager);
            int textureMatches = 0;
            foreach (var texture in objects.OfType<Texture2D>())
            {
                var s = texture.m_StreamData;
                if (string.IsNullOrEmpty(s?.path)) continue;
                string id = QtsVFSFile.Compute(s.path, true).ToString();
                var matches = manager.ContainerEntries.Where(e => e.Id == id).ToArray();
                if (matches.Length == 1 && s.offset >= 0 && s.offset <= matches[0].Data.LongLength - s.size) textureMatches++;
            }
            var schema = new {
                layout, links,
                entries = manager.ContainerEntries.Select(e => new { e.Id, e.Kind, bytes = e.Data.Length, hash = Hash(e.Data) }).ToArray(),
                files = manager.assetsFileList.Select(f => new {
                    f.fileName, f.unityVersion,
                    externals = f.m_Externals.Select(e => new { e.guid, e.type, e.pathName }).ToArray(),
                    objects = f.m_Objects.Select(o => new { id = o.m_PathID.ToString(), o.byteStart, o.byteSize, o.typeID, o.classID }).ToArray(),
                    types = f.m_Types.Select(t => new { t.classID, t.m_ScriptTypeIndex, t.m_ScriptID, t.m_OldTypeHash }).ToArray()
                }).ToArray()
            };
            return new(Hash(JsonSerializer.SerializeToUtf8Bytes(schema)), Hash(JsonSerializer.SerializeToUtf8Bytes(new { schema.layout, schema.links, schema.files })), paths.Length, manager.ContainerEntries.Count,
                manager.assetsFileList.Count, objects.Length, manager.assetsFileList.Count(f => f.m_Types.All(t => t.m_Type?.m_Nodes?.Count is null or 0)),
                objects.Count(o => o.GetType() == typeof(Obj)), links.Count(l => l.Status == "resolved"), links.Count(l => l.Status == "unresolved"),
                textureMatches, codecs, links.ToArray(), [.. log.Warnings,
                    "Only parsed public pointer fields are inspected; opaque custom data and discarded parser fields are not a complete call graph.",
                    "Unresolved GUID-only references are retained as unresolved; no global PathID guess is used.",
                    "A zero-change byte-preserving output does not validate a modified QTS writer or game-runtime compatibility."]);
        }
        finally { manager.Clear(); Logger.Default = previous; }
    }

    static bool IsQts(string path)
    {
        using var stream = File.OpenRead(path); Span<byte> head = stackalloc byte[8];
        return stream.Read(head) == 8 && head.SequenceEqual(new byte[] { 1, 0, 0, 2, 1, 2, 3, 4 });
    }
    static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    static void Walk(object? value, Obj owner, string field, HashSet<object> seen, int depth, List<ReferenceLink> rows, AssetsManager manager)
    {
        if (value is null || depth > 18) return;
        var type = value.GetType();
        if (type.IsPrimitive || type.IsEnum || value is string or decimal or byte[]) return;
        if (!type.IsValueType && !seen.Add(value)) return;
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(PPtr<>))
        {
            int fileId = (int)type.GetField("m_FileID")!.GetValue(value)!;
            long pathId = (long)type.GetField("m_PathID")!.GetValue(value)!;
            var external = fileId > 0 && fileId <= owner.assetsFile.m_Externals.Count ? owner.assetsFile.m_Externals[fileId - 1] : null;
            var target = fileId == 0 ? owner.assetsFile : external is null || string.IsNullOrEmpty(external.fileName) ? null
                : manager.assetsFileList.FirstOrDefault(f => f.fileName.Equals(external.fileName, StringComparison.OrdinalIgnoreCase));
            string status = pathId == 0 ? "null" : target?.ObjectsDic.ContainsKey(pathId) == true ? "resolved" : "unresolved";
            rows.Add(new(owner.assetsFile.fileName, owner.m_PathID.ToString(), field, fileId, pathId.ToString(), external?.guid.ToString(), external?.pathName, status));
            return;
        }
        if (value is IEnumerable sequence) { int i = 0; foreach (var item in sequence) Walk(item, owner, field + "[" + i++ + "]", seen, depth + 1, rows, manager); return; }
        if (type.Namespace is null || (!type.Namespace.StartsWith("AssetStudio") && !type.Namespace.StartsWith("System.Collections.Generic"))) return;
        foreach (var member in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (member.Name is "assetsFile" or "reader" or "serializedType" or "version" or "assetsManager") continue;
            var next = member.GetValue(value); if (next is Obj && next != owner) continue;
            Walk(next, owner, field.Length == 0 ? member.Name : field + "." + member.Name, seen, depth + 1, rows, manager);
        }
    }
    sealed class InspectionLog : ILogger
    {
        public List<string> Errors { get; } = [];
        public List<string> Warnings { get; } = [];
        public void Log(LoggerEvent level, string message) { if (level == LoggerEvent.Error) Errors.Add(message); else if (level == LoggerEvent.Warning) Warnings.Add(message); }
    }
}
