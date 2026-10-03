using System.Text.Json;
using Hok.Rebuild;

if (args.Length != 1) throw new ArgumentException("Pass the HOK BetaStudio project root.");
var root = Path.GetFullPath(args[0]);
var scratch = Path.Combine(root, ".cache", "rebuild-baseline-tests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(scratch);
var checks = new List<string>();
void Assert(bool value, string text) { if (!value) throw new Exception(text); checks.Add(text); }
void Reject(Action action, string text)
{
    try { action(); }
    catch (Exception e) when (e is IOException or InvalidDataException or OperationCanceledException) { checks.Add(text); return; }
    throw new Exception("Expected rejection: " + text);
}
var fixture = Path.Combine(scratch, "fixture");
Directory.CreateDirectory(Path.Combine(fixture, "nested", "empty"));
File.WriteAllBytes(Path.Combine(fixture, "package.db"), [1, 2, 3]);
File.WriteAllBytes(Path.Combine(fixture, "record.bytes"), [4, 5]);
File.WriteAllBytes(Path.Combine(fixture, "nested", "unknown-file.bin"), [6, 7, 8]);
var baseline = PackageBaseline.Capture(fixture);
var copied = PackageBaseline.WriteUnchanged(baseline, Path.Combine(scratch, "fixture-output"));
Assert(copied.Files.Length == 3 && copied.Directories.Length == 2, "Unknown companion files and empty directories are retained");
PackageBaseline.Verify(baseline, copied.SourceDirectory);
checks.Add("All fixture file SHA-256 values match");
Reject(() => PackageBaseline.WriteUnchanged(baseline, fixture), "Source overwrite rejected");
Reject(() => PackageBaseline.WriteUnchanged(baseline, Path.Combine(fixture, "child")), "Output nested under source rejected");
Reject(() => PackageBaseline.WriteUnchanged(baseline, scratch), "Source nested under output rejected");
Reject(() => PackageBaseline.WriteUnchanged(baseline, copied.SourceDirectory), "Existing output rejected");
using (var cancelled = new CancellationTokenSource())
{
    cancelled.Cancel();
    var path = Path.Combine(scratch, "cancelled");
    Reject(() => PackageBaseline.WriteUnchanged(baseline, path, cancelled.Token), "Cancellation prevents final output");
    Assert(!Directory.Exists(path), "Cancelled output absent");
}
File.WriteAllBytes(Path.Combine(fixture, "package.db"), [3, 2, 1]);
Reject(() => PackageBaseline.WriteUnchanged(baseline, Path.Combine(scratch, "changed")), "Equal-size source mutation rejected by SHA-256");
File.WriteAllBytes(Path.Combine(fixture, "package.db"), [1, 2, 3]);
File.WriteAllBytes(Path.Combine(fixture, "added.bin"), [9]);
Reject(() => PackageBaseline.WriteUnchanged(baseline, Path.Combine(scratch, "added")), "Added source file invalidates snapshot");
var samples = new List<object>();
foreach (var name in new[] { "3200010500", "3200010504" })
{
    string input = Path.Combine(root, name);
    var manifest = PackageBaseline.Capture(input);
    var before = ReferenceInspection.Inspect(input);
    var first = PackageBaseline.WriteUnchanged(manifest, Path.Combine(scratch, name + "-a"));
    var second = PackageBaseline.WriteUnchanged(manifest, Path.Combine(scratch, name + "-b"));
    PackageBaseline.Verify(manifest, input);
    PackageBaseline.Verify(first, second.SourceDirectory);
    var after = ReferenceInspection.Inspect(first.SourceDirectory);
    var again = ReferenceInspection.Inspect(second.SourceDirectory);
    Assert(before.StructuralSha256 == after.StructuralSha256 && before.StructuralSha256 == again.StructuralSha256,
        name + ": two outputs retain QTS layout, all decoded entries, object metadata, types and inspected references");
    Assert(before.ContainerEntries == after.ContainerEntries && before.Objects == after.Objects,
        name + ": actual parser inventory unchanged");
    Assert(first.Files.SequenceEqual(second.Files), name + ": repeated outputs byte-identical for every file");
    samples.Add(new {
        name, inputFiles = manifest.Files, outputs = new[] { first.SourceDirectory, second.SourceDirectory },
        sourceHashesUnchanged = true, repeatedOutputHashesIdentical = true, structuralChecksEqual = true,
        before.DbFiles, before.ContainerEntries, before.SerializedFiles, before.Objects,
        before.MissingTypeTrees, before.GenericObjects, before.ResolvedReferences, before.UnresolvedReferences,
        before.TextureStreamsMatched, before.Codecs, before.StructuralSha256,
        warning = "Byte-preserving passthrough only. No modified block was encoded and no game-runtime test was performed."
    });
    File.WriteAllText(Path.Combine(scratch, name + "-references.json"), JsonSerializer.Serialize(before, new JsonSerializerOptions { WriteIndented = true }));
}
var report = new { date = DateTimeOffset.UtcNow, stage = "phase-1-baseline-only", passed = checks.Count, checks, samples, scratch, modifiedRebuildReady = false };
var output = Path.Combine(root, "planning", "rebuild-baseline-test-report.json");
File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(new { output, report.stage, report.passed, samples, report.modifiedRebuildReady }));
