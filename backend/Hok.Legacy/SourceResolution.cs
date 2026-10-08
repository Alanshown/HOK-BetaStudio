using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AssetStudio
{
    public partial class AssetsManager
    {
        private readonly Dictionary<string, BinaryReader> diskResourceReaders = new Dictionary<string, BinaryReader>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> ambiguousScopedResources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<BinaryReader> retiredResourceReaders = new List<BinaryReader>();
        private readonly Dictionary<(SerializedFile, FileIdentifier), (int Count, SerializedFile File)> resolvedExternals = new Dictionary<(SerializedFile, FileIdentifier), (int, SerializedFile)>();
        private int referenceIndexCount = -1;
        private readonly Dictionary<string, Dictionary<long, SerializedFile>> sourceObjects = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<SerializedFile>> filesByName = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, SerializedFile> filesByIdentity = new(StringComparer.OrdinalIgnoreCase);
        private void ClearReferenceIndex()
        {
            referenceIndexCount = -1; sourceObjects.Clear(); filesByName.Clear(); filesByIdentity.Clear();
        }
        private void EnsureReferenceIndex()
        {
            if (referenceIndexCount == assetsFileList.Count) return;
            ClearReferenceIndex();
            foreach (var file in assetsFileList)
            {
                if (!filesByIdentity.TryAdd(file.fullName, file)) filesByIdentity[file.fullName] = null;
                if (!filesByName.TryGetValue(file.fileName, out var names)) filesByName.Add(file.fileName, names = new());
                names.Add(file);
                if (string.IsNullOrEmpty(file.originalPath)) continue;
                if (!sourceObjects.TryGetValue(file.originalPath, out var objects)) sourceObjects.Add(file.originalPath, objects = new());
                foreach (var metadata in file.m_Objects)
                    if (!objects.TryAdd(metadata.m_PathID, file)) objects[metadata.m_PathID] = null;
            }
            referenceIndexCount = assetsFileList.Count;
        }
        internal static string ResourceKey(string source, string name) => Path.GetFullPath(source) + "|" + name.Replace('\\', '/');
        private static string ReferenceName(string path) => Path.GetFileName(path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar));
        private static bool HasExplicitDirectory(string reference) => !reference.StartsWith("archive:/", StringComparison.OrdinalIgnoreCase) && (reference.Contains('/') || reference.Contains('\\'));
        private static string LocalCandidate(string directory, string reference)
        {
            if (string.IsNullOrEmpty(reference)) return null;
            // Unity archive paths are virtual; only their leaf belongs on disk.
            if (reference.StartsWith("archive:/", StringComparison.OrdinalIgnoreCase)) reference = ReferenceName(reference);
            reference = reference.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
            if (reference.IndexOfAny(new[] { '*', '?' }) >= 0) return null;
            try
            {
                var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                var candidate = Path.GetFullPath(Path.Combine(root, reference));
                return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? candidate : null;
            }
            catch (ArgumentException) { return null; }
            catch (NotSupportedException) { return null; }
        }
        // Direct/explicit local path wins. A recursive fallback is allowed only
        // when unique, and never searches sibling package directories.
        internal static string FindLocalFile(string directory, string reference)
        {
            var candidate = LocalCandidate(directory, reference);
            if (candidate == null || !Directory.Exists(directory)) return null;
            if (File.Exists(candidate)) return candidate;
            if (HasExplicitDirectory(reference)) return null;
            var name = ReferenceName(reference);
            if (string.IsNullOrEmpty(name)) return null;
            var matches = Directory.EnumerateFiles(directory, name, new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = false, AttributesToSkip = FileAttributes.ReparsePoint }).Take(2).ToArray();
            if (matches.Length == 1) return Path.GetFullPath(matches[0]);
            if (matches.Length > 1) Logger.Warning($"Ambiguous local dependency {reference} under {directory}; no file selected.");
            return null;
        }
        internal BinaryReader OpenDiskResource(string path)
        {
            var fullPath = Path.GetFullPath(path);
            if (!diskResourceReaders.TryGetValue(fullPath, out var reader))
                diskResourceReaders.Add(fullPath, reader = new BinaryReader(File.OpenRead(fullPath)));
            return reader;
        }
        internal SerializedFile ResolveExternal(SerializedFile owner, FileIdentifier external)
        {
            var key = (owner, external);
            if (resolvedExternals.TryGetValue(key, out var cached) && cached.Count == assetsFileList.Count) return cached.File;
            var result = ResolveExternalUncached(owner, external);
            resolvedExternals[key] = (assetsFileList.Count, result);
            return result;
        }
        internal SerializedFile ResolveHokObjectReference(SerializedFile owner, long pathId)
        {
            if (owner.ObjectsDic.ContainsKey(pathId)) return owner;
            if (string.IsNullOrEmpty(owner.originalPath)) return null;
            // QTS can split a prefab and its clips into separate SerializedFiles
            // with GUID-only external slots. A unique PathID in the exact same
            // source DB is safe to resolve; other DBs/directories never qualify.
            EnsureReferenceIndex();
            if (sourceObjects.TryGetValue(owner.originalPath, out var objects) && objects.TryGetValue(pathId, out var match))
            {
                if (match != null) return match;
                Logger.Warning($"Ambiguous HOK object reference {pathId} from {owner.fullName}; no target selected.");
            }
            return null;
        }
        private SerializedFile ResolveExternalUncached(SerializedFile owner, FileIdentifier external)
        {
            string name = external.fileName;
            if (string.IsNullOrEmpty(name)) return null;
            string reference = string.IsNullOrEmpty(external.pathName) ? name : external.pathName;
            // A Set() reference to an already loaded exact identity needs no guess.
            EnsureReferenceIndex();
            if (filesByIdentity.TryGetValue(reference, out var explicitMatch) && explicitMatch != null) return explicitMatch;
            var matches = filesByName.TryGetValue(name, out var namedFiles) ? namedFiles.ToArray() : Array.Empty<SerializedFile>();
            if (owner.originalPath != null)
            {
                matches = matches.Where(f => string.Equals(f.originalPath, owner.originalPath, StringComparison.OrdinalIgnoreCase)).ToArray();
                var candidate = LocalCandidate(Path.GetDirectoryName(owner.fullName), reference);
                var exact = matches.Where(f => string.Equals(f.fullName, candidate, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (exact.Length == 1) return exact[0];
                if (HasExplicitDirectory(reference)) return null;
            }
            else
            {
                string directory = Path.GetDirectoryName(owner.fullName);
                var candidate = LocalCandidate(directory, reference);
                var exact = matches.Where(f => string.Equals(f.fullName, candidate, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (exact.Length == 1) return exact[0];
                if (candidate == null || HasExplicitDirectory(reference)) return null;
                var prefix = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                matches = matches.Where(f => f.originalPath == null && f.fullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToArray();
            }
            if (matches.Length == 1) return matches[0];
            if (matches.Length > 1) Logger.Warning($"Ambiguous external {reference} from {owner.fullName}; no reference target selected.");
            return null;
        }
    }
}
