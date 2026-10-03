using AssetStudio;
using Hok.Rebuild;
using System.Security.Cryptography;
using System.Text.Json;
using Obj = AssetStudio.Object;

namespace Hok.Worker;

internal sealed class ReplacementSession
{
    record Pending(string AssetId, string Name, ContainerEntry Entry, int Offset, byte[] Bytes, string Filename);
    readonly AssetsManager manager;
    readonly Dictionary<string, Obj> objects;
    readonly Dictionary<string, ResourceAsset> resources;
    readonly string[] sources;
    readonly Dictionary<string, string> inputHashes;
    readonly string cache;
    Dictionary<string, Pending> pending = [];
    BaselineManifest? baseline;
    Inspection? inspection;

    public ReplacementSession(AssetsManager manager, Dictionary<string, Obj> objects, Dictionary<string, ResourceAsset> resources, string[] sources, string cache)
    {
        this.manager = manager; this.objects = objects; this.resources = resources; this.sources = sources; this.cache = cache;
        inputHashes = sources.ToDictionary(Path.GetFullPath, HashFile, StringComparer.OrdinalIgnoreCase);
    }
    static string HashFile(string path) { using var file = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)); }
    public object State() => new { beta = true, count = pending.Count, items = pending.Values.Select(p => new { assetId = p.AssetId, p.Name, replacement = p.Filename, bytes = p.Bytes.Length }).ToArray(), warning = ExperimentalSlotWriter.Warning };
    void EnsureBaseline()
    {
        foreach (var source in inputHashes) if (HashFile(source.Key) != source.Value) throw new IOException("Source DB changed; reload the workspace.");
        if (baseline is not null) { PackageBaseline.Verify(baseline, baseline.SourceDirectory); return; }
        var directories = sources.Select(p => Path.GetDirectoryName(Path.GetFullPath(p))!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (directories.Length != 1) throw new InvalidDataException("Beta rebuild requires a single complete package directory. Select one DB group first.");
        var captured = PackageBaseline.Capture(directories[0]);
        var inspected = ReferenceInspection.Inspect(directories[0]);
        baseline = captured; inspection = inspected;
    }

    public object Stage(string id, string path)
    {
        EnsureBaseline();
        var info = new FileInfo(path);
        if (!info.Exists || info.Length > 64 * 1024 * 1024) throw new InvalidDataException("Replacement file missing or larger than 64 MiB.");
        ContainerEntry entry; int offset; int length; string name;
        if (objects.TryGetValue(id, out var obj))
        {
            if (obj.GetType() == typeof(Obj) || obj is MonoBehaviour) throw new NotSupportedException("Beta cannot safely edit opaque/custom object layouts. Replace only supported raw objects or an identified resource stream.");
            var entries = manager.ContainerEntries.Where(e => e.Id == obj.assetsFile.fileName).ToArray();
            if (entries.Length != 1) throw new InvalidDataException("Cannot uniquely identify this object's QTS container.");
            entry = entries[0]; offset = checked((int)obj.reader.byteStart); length = checked((int)obj.byteSize); name = obj.Name;
        }
        else if (resources.TryGetValue(id, out var resource))
        {
            if (resource.Parent is not null || resource.Type is "PackageMetadata" or "CompressedQtsChunk" or "UnparsedObject")
                throw new NotSupportedException("Beta cannot replace nested media, package metadata or undecoded chunks. Use the complete original container where supported.");
            var entries = manager.ContainerEntries.Where(e => ReferenceEquals(e.Data, resource.Data)).ToArray();
            if (entries.Length != 1) throw new InvalidDataException("No unique original QTS entry for this resource.");
            entry = entries[0]; offset = 0; length = entry.Data.Length; name = resource.Name;
            if (resource.Type == "SerializedFile") throw new NotSupportedException("Beta whole-SerializedFile replacement is disabled; select a supported individual object instead.");
        }
        else throw new InvalidOperationException("Asset no longer loaded.");
        if (info.Length != length) throw new InvalidDataException($"Beta needs the original binary format and exactly {length} bytes. PNG/OBJ/MP3 conversion files are not raw game data.");
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length != length) throw new IOException("Replacement changed while reading.");
        if (resources.TryGetValue(id, out var media) && media.Type is "WwiseBank" or "WwisePackage")
        {
            if (ResourceAsset.Detect(bytes, entry.Kind).type != media.Type) throw new InvalidDataException("Replacement container type differs.");
            _ = WwiseIndex.Expand(media with { Data = bytes }).ToArray();
        }
        foreach (var other in pending.Values.Where(p => p.AssetId != id && ReferenceEquals(p.Entry, entry)))
            if (offset < other.Offset + other.Bytes.Length && other.Offset < offset + length) throw new InvalidDataException("This replacement overlaps another staged item.");
        var candidate = new Dictionary<string, Pending>(pending) { [id] = new(id, name, entry, offset, bytes, info.Name) };
        if (candidate.Values.Sum(p => (long)p.Bytes.Length) > 256L * 1024 * 1024) throw new InvalidDataException("Beta staging limit: 256 MiB.");
        // Validate before committing the new staging state; failure retains every
        // previous successful replacement, including the previous version of id.
        var testPath = Path.Combine(cache, "replace-check-" + Guid.NewGuid().ToString("N"));
        try { WriteCandidate(candidate, testPath); pending = candidate; return State(); }
        finally { try { if (Directory.Exists(testPath)) Directory.Delete(testPath, true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Console.Error.WriteLine("Owned candidate cleanup deferred: " + e.Message); } }
    }

    Dictionary<ContainerEntry, byte[]> Overlays(Dictionary<string, Pending> items)
    {
        var overlays = new Dictionary<ContainerEntry, byte[]>();
        foreach (var p in items.Values)
        {
            if (!overlays.TryGetValue(p.Entry, out var data)) overlays.Add(p.Entry, data = (byte[])p.Entry.Data.Clone());
            p.Bytes.CopyTo(data, p.Offset);
        }
        return overlays;
    }
    void WriteCandidate(Dictionary<string, Pending> items, string destination)
    {
        EnsureBaseline();
        var overlays = Overlays(items);
        var plans = overlays.ToDictionary(p => p.Key, p => ExperimentalSlotWriter.Prepare(p.Key.Source, ulong.Parse(p.Key.Id), p.Key.Data, p.Value));
        PackageBaseline.WriteUnchanged(baseline!, destination, publishAtomically: false);
        foreach (var group in plans.GroupBy(p => p.Key.Source, StringComparer.OrdinalIgnoreCase))
            ExperimentalSlotWriter.ApplyToNewCopy(Path.Combine(destination, Path.GetRelativePath(baseline!.SourceDirectory, group.Key)), group.SelectMany(p => p.Value));
        var after = ReferenceInspection.Inspect(destination);
        if (after.ReferenceSchemaSha256 != inspection!.ReferenceSchemaSha256) throw new InvalidDataException("Replacement changed object metadata, inspected references or QTS layout; candidate rejected.");
        // The complete decoded file set, not just the selected item, must match.
        var verify = new AssetsManager { Game = GameManager.GetGame(GameType.HonorOfKings) };
        try
        {
            verify.LoadFilesReadOnly(sources.Select(s => Path.Combine(destination, Path.GetRelativePath(baseline!.SourceDirectory, s))).ToArray());
            if (verify.ContainerEntries.Count != manager.ContainerEntries.Count) throw new InvalidDataException("Rebuild lost or added container files.");
            foreach (var original in manager.ContainerEntries)
            {
                var relative = Path.GetRelativePath(baseline!.SourceDirectory, original.Source);
                var matches = verify.ContainerEntries.Where(e => e.Id == original.Id && Path.GetRelativePath(destination, e.Source) == relative).ToArray();
                if (matches.Length != 1 || !matches[0].Data.AsSpan().SequenceEqual(overlays.GetValueOrDefault(original, original.Data)))
                    throw new InvalidDataException("Decoded contents differ from the staged plan: " + original.Id);
            }
        }
        finally { verify.Clear(); }
        PackageBaseline.Verify(baseline!, baseline!.SourceDirectory);
    }

    public object Build(string parent)
    {
        if (pending.Count == 0) throw new InvalidOperationException("No staged replacements.");
        EnsureBaseline(); parent = Path.GetFullPath(parent);
        var tag = Path.GetFileName(baseline!.SourceDirectory) + "-rebuilt-beta-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6];
        var output = Path.Combine(parent, tag);
        var partial = Path.Combine(parent, "." + tag + ".partial");
        try
        {
            WriteCandidate(pending, partial);
            var outputManifest = PackageBaseline.Capture(partial);
            PackageBaseline.CommitDirectory(partial, output);
            var reportPath = Path.Combine(parent, tag + "-report.json");
            var report = new { beta = true, recommended = false, output, count = pending.Count, warning = ExperimentalSlotWriter.Warning,
                originalFiles = baseline.Files, outputFiles = outputManifest.Files, referencesPreserved = true, completeDecodedContentsVerified = true,
                gameCompatibilityVerified = false, changes = pending.Values.Select(p => new { p.AssetId, p.Name, p.Filename, entry = p.Entry.Id, p.Offset, bytes = p.Bytes.Length }).ToArray() };
            using var reportFile = new FileStream(reportPath, FileMode.CreateNew, FileAccess.Write);
            JsonSerializer.Serialize(reportFile, report, new JsonSerializerOptions(Program.Json) { WriteIndented = true });
            return new { output, reportPath, count = pending.Count, beta = true, warning = ExperimentalSlotWriter.Warning };
        }
        catch (Exception e) { throw new IOException("Beta rebuild failed; original package untouched. Candidate (if created): " + partial, e); }
    }
}
