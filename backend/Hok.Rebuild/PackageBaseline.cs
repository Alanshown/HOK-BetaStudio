using System.Security.Cryptography;

namespace Hok.Rebuild;

public sealed record BaselineFile(string RelativePath, long Bytes, string Sha256);
public sealed record BaselineManifest(string SourceDirectory, BaselineFile[] Files, string[] Directories);

/// <summary>
/// Byte-preserving zero-change output. This is deliberately not a QTS encoder:
/// modified outputs must remain disabled until a format-aware writer is verified.
/// </summary>
public static class PackageBaseline
{
    public static BaselineManifest Capture(string source, CancellationToken token = default)
    {
        source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
        RequireOrdinaryPath(source);
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException(source);
        var files = new List<BaselineFile>();
        var directories = new List<string>();
        var pending = new Stack<string>();
        pending.Push(source);
        while (pending.TryPop(out var directory))
        {
            token.ThrowIfCancellationRequested();
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory).Order(StringComparer.Ordinal))
            {
                token.ThrowIfCancellationRequested();
                RequireOrdinaryPath(entry);
                if (Directory.Exists(entry))
                {
                    directories.Add(Path.GetRelativePath(source, entry));
                    pending.Push(entry);
                }
                else
                {
                    using var stream = new FileStream(entry, FileMode.Open, FileAccess.Read, FileShare.Read);
                    files.Add(new(Path.GetRelativePath(source, entry), stream.Length, Convert.ToHexString(SHA256.HashData(stream))));
                }
            }
        }
        if (files.Count == 0) throw new InvalidDataException("Package folder is empty.");
        return new(source, files.OrderBy(f => f.RelativePath, StringComparer.Ordinal).ToArray(), directories.Order(StringComparer.Ordinal).ToArray());
    }

    public static void Verify(BaselineManifest expected, string directory, CancellationToken token = default)
    {
        var actual = Capture(directory, token);
        if (!expected.Files.SequenceEqual(actual.Files) || !expected.Directories.SequenceEqual(actual.Directories))
            throw new InvalidDataException("Package contents changed: inventory, size or SHA-256 does not match the baseline.");
    }

    public static BaselineManifest WriteUnchanged(BaselineManifest baseline, string outputDirectory, CancellationToken token = default, bool publishAtomically = true)
    {
        var source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(baseline.SourceDirectory));
        var output = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputDirectory));
        if (Within(output, source) || Within(source, output)) throw new IOException("Output and source must be separate, non-overlapping directories.");
        RequireOrdinaryPath(output);
        if (File.Exists(output) || Directory.Exists(output)) throw new IOException("Output already exists; overwriting is prohibited.");
        Verify(baseline, source, token);
        string parent = Path.GetDirectoryName(output) ?? throw new IOException("Output cannot be a drive root.");
        Directory.CreateDirectory(parent);
        // Private validation copies already have a unique non-published path;
        // they need no rename (not all encrypted Windows cache roots allow it).
        string staging = publishAtomically ? Path.Combine(parent, ".hok-zero-change-" + Guid.NewGuid().ToString("N") + ".partial") : output;
        Directory.CreateDirectory(staging);
        // Do not delete a failed staging directory blindly. It is never committed
        // as the final package and its unique path is included in the exception.
        try
        {
            foreach (var directory in baseline.Directories)
                Directory.CreateDirectory(Child(staging, directory));
            foreach (var file in baseline.Files)
            {
                token.ThrowIfCancellationRequested();
                string input = Child(source, file.RelativePath), destination = Child(staging, file.RelativePath);
                RequireOrdinaryPath(input);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                using var original = new FileStream(input, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var copy = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                var buffer = new byte[1024 * 1024];
                int read;
                while ((read = original.Read(buffer)) > 0)
                {
                    token.ThrowIfCancellationRequested();
                    copy.Write(buffer, 0, read);
                }
                copy.Flush(true);
            }
            Verify(baseline, staging, token);
            Verify(baseline, source, token);
            token.ThrowIfCancellationRequested();
            RequireOrdinaryPath(output);
            if (publishAtomically) CommitDirectory(staging, output, token);
            return baseline with { SourceDirectory = output };
        }
        catch (Exception e)
        {
            throw new IOException($"Zero-change output not committed. Incomplete staging retained at: {staging}", e);
        }
    }

    public static void CommitDirectory(string staging, string output, CancellationToken token = default)
    {
        // Windows scanners may briefly hold newly written files without delete
        // sharing. Retry the atomic rename; never fall back to merging directories.
        for (int attempt = 0; ; attempt++)
        {
            token.ThrowIfCancellationRequested();
            if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Output already exists; overwrite prohibited.");
            try { Directory.Move(staging, output); return; }
            catch (IOException) when (attempt < 6) { Thread.Sleep(50 * (1 << attempt)); }
        }
    }

    static string Child(string root, string relative)
    {
        if (Path.IsPathRooted(relative)) throw new IOException("Absolute paths are not valid manifest entries.");
        var full = Path.GetFullPath(Path.Combine(root, relative));
        if (full.Equals(root, StringComparison.OrdinalIgnoreCase) || !Within(full, root))
            throw new IOException("Manifest entry escapes its package root.");
        return full;
    }

    static bool Within(string path, string root) => path.Equals(root, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    static void RequireOrdinaryPath(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
                throw new IOException("Symlinks and junctions are not allowed in package input/output paths: " + current);
    }
}
