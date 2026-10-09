using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using ZstdSharp;
using static AssetStudio.ImportHelper;

namespace AssetStudio
{
    public partial class AssetsManager
    {
        public Game Game;
        public bool Silent = false;
        public bool SkipProcess = false;
        public bool ResolveDependencies = false;        
        public string SpecifyUnityVersion;
        public CancellationTokenSource tokenSource = new CancellationTokenSource();
        public List<SerializedFile> assetsFileList = new List<SerializedFile>();
        public List<ContainerEntry> ContainerEntries { get; } = new List<ContainerEntry>();
        public IEnumerable<KeyValuePair<string, BinaryReader>> ResourceFiles => scopedResourceReaders;

        internal Dictionary<string, int> assetsFileIndexCache = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        internal Dictionary<string, BinaryReader> resourceFileReaders = new Dictionary<string, BinaryReader>(StringComparer.OrdinalIgnoreCase);
        internal Dictionary<string, BinaryReader> scopedResourceReaders = new Dictionary<string, BinaryReader>(StringComparer.OrdinalIgnoreCase);
        internal HashSet<string> ambiguousResourceNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        internal void RegisterResource(string name, BinaryReader reader, string source)
        {
            var key = ResourceKey(source, name);
            if (scopedResourceReaders.TryGetValue(key, out var existing) && !ReferenceEquals(existing, reader))
            {
                // Keep both readers alive for cleanup, but never choose an alias collision.
                ambiguousScopedResources.Add(key);
                retiredResourceReaders.Add(reader);
                Logger.Warning($"Ambiguous resource in container {source}: {name}");
                return;
            }
            scopedResourceReaders[key] = reader;
            if (!resourceFileReaders.TryAdd(name, reader))
            {
                ambiguousResourceNames.Add(name);
                Logger.Warning($"Resource alias {name} occurs in several sources; package-qualified lookup is required.");
            }
        }
        internal bool TryGetResource(SerializedFile file, string name, out BinaryReader reader)
        {
            var key = ResourceKey(file.originalPath ?? file.fullName, name);
            if (ambiguousScopedResources.Contains(key)) throw new InvalidDataException($"Ambiguous resource in {file.originalPath ?? file.fullName}: {name}");
            return scopedResourceReaders.TryGetValue(key, out reader);
        }

        internal List<string> importFiles = new List<string>();
        internal HashSet<string> importFilesHash = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        internal HashSet<string> noexistFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        internal HashSet<string> assetsFileListHash = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public void LoadFilesReadOnly(params string[] files)
        {
            if (files == null || files.Length == 0) return;
            Load(files.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        }

        public void LoadFiles(params string[] files)
        {
            if (Silent)
            {
                Logger.Silent = true;
                Progress.Silent = true;
            }

            var path = Path.GetDirectoryName(Path.GetFullPath(files[0]));
            MergeSplitAssets(path);
            var toReadFile = ProcessingSplitFiles(files.ToList());
            if (ResolveDependencies)
                toReadFile = AssetsHelper.ProcessDependencies(toReadFile);
            Load(toReadFile);

            if (Silent)
            {
                Logger.Silent = false;
                Progress.Silent = false;
            }
        }

        public void LoadFolder(string path)
        {
            if (Silent)
            {
                Logger.Silent = true;
                Progress.Silent = true;
            }

            MergeSplitAssets(path, true);
            var files = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories).ToList();
            var toReadFile = ProcessingSplitFiles(files);
            Load(toReadFile);

            if (Silent)
            {
                Logger.Silent = false;
                Progress.Silent = false;
            }
        }

        private void Load(string[] files)
        {
            foreach (var file in files)
            {
                Logger.Verbose($"caching {file} path and name to filter out duplicates");
                importFiles.Add(file);
                importFilesHash.Add(Path.GetFullPath(file));
            }

            Progress.Reset();
            //use a for loop because list size can change
            for (var i = 0; i < importFiles.Count; i++)
            {
                try { LoadFile(importFiles[i]); }
                catch (Exception e) { Logger.Error($"Unable to open {importFiles[i]}; continuing with other inputs", e); }
                Progress.Report(i + 1, importFiles.Count);
                if (tokenSource.IsCancellationRequested)
                {
                    Logger.Info("Loading files has been aborted !!");
                    break;
                }
            }

            importFiles.Clear();
            importFilesHash.Clear();
            noexistFiles.Clear();
            assetsFileListHash.Clear();
            AssetsHelper.ClearOffsets();

            if (!SkipProcess)
            {
                ReadAssets();
                ProcessAssets();
            }
        }

        private void LoadFile(string fullName)
        {
            var reader = new FileReader(fullName);
            reader = reader.PreProcessing(Game);
            LoadFile(reader);
        }

        private void LoadFile(FileReader reader)
        {
            switch (reader.FileType)
            {
                case FileType.AssetsFile:
                    LoadAssetsFile(reader);
                    break;
                case FileType.BundleFile:
                    LoadBundleFile(reader);
                    break;
                case FileType.WebFile:
                    LoadWebFile(reader);
                    break;
                case FileType.GZipFile:
                    LoadFile(DecompressGZip(reader));
                    break;
                case FileType.BrotliFile:
                    LoadFile(DecompressBrotli(reader));
                    break;
                case FileType.ZipFile:
                    LoadZipFile(reader);
                    break;
                case FileType.BlockFile:
                    LoadBlockFile(reader);
                    break;
                case FileType.BlkFile:
                    LoadBlkFile(reader);
                    break;
                case FileType.MhyFile:
                    LoadMhyFile(reader);
                    break;
                case FileType.QtsVFSFile:
                    LoadQtsVFS(reader);
                    break;
                default:
                    Logger.Warning($"Unsupported top-level file {reader.FullPath} ({reader.FileType}); preserving raw bytes only");
                    if (reader.Length > 512L * 1024 * 1024) { reader.Dispose(); throw new InvalidDataException("Raw resource exceeds 512 MiB limit"); }
                    reader.Position = 0;
                    ContainerEntries.Add(new ContainerEntry(reader.FileName, reader.FullPath, reader.ReadBytes((int)reader.Length), "UnparsedFile", "No typed container parser; raw source retained."));
                    reader.Dispose();
                    break;
            }
        }

        private void LoadAssetsFile(FileReader reader)
        {
            if (!assetsFileListHash.Contains(reader.FullPath))
            {
                Logger.Info($"Loading {reader.FullPath}");
                try
                {
                    var assetsFile = new SerializedFile(reader, this);
                    CheckStrippedVersion(assetsFile);
                    assetsFileList.Add(assetsFile);
                    assetsFileListHash.Add(reader.FullPath);
                    serializedNameCounts.TryGetValue(assetsFile.fileName,out int nameCount);serializedNameCounts[assetsFile.fileName]=nameCount+1;
                    if (nameCount > 0)
                        Logger.Warning($"Distinct SerializedFiles share name {assetsFile.fileName}; preserving all source identities. External references must resolve unambiguously.");

                    foreach (var sharedFile in assetsFile.m_Externals)
                    {
                        Logger.Verbose($"{assetsFile.fileName} needs external file {sharedFile.fileName}, attempting to look it up...");
                        var sharedFileName = sharedFile.fileName;
                        if (Path.GetFileName(sharedFileName) != sharedFileName || sharedFileName.IndexOfAny(new[] { '*', '?', ':' }) >= 0)
                        {
                            Logger.Warning("Rejected unsafe external reference name");
                            continue;
                        }
                        if (Game.Type.IsHonorOfKings() && string.IsNullOrEmpty(sharedFileName))
                            continue;

                        var sharedFilePath = FindLocalFile(Path.GetDirectoryName(reader.FullPath), sharedFile.pathName ?? sharedFileName);
                        if (sharedFilePath != null && importFilesHash.Add(sharedFilePath)) importFiles.Add(sharedFilePath);
                    }
                }
                catch (Exception e)
                {
                    Logger.Error($"Error while reading assets file {reader.FullPath}", e);
                    reader.Dispose();
                }
            }
            else
            {
                Logger.Info($"Skipping {reader.FullPath}");
                reader.Dispose();
            }
        }

        private void LoadAssetsFromMemory(FileReader reader, string originalPath, string unityVersion = null, long originalOffset = 0)
        {
            Logger.Verbose($"Loading asset file {reader.FileName} with version {unityVersion} from {originalPath} at offset 0x{originalOffset:X8}");
            if (!assetsFileListHash.Contains(reader.FullPath))
            {
                try
                {
                    var assetsFile = new SerializedFile(reader, this);
                    assetsFile.originalPath = originalPath;
                    assetsFile.offset = originalOffset;
                    if (!string.IsNullOrEmpty(unityVersion) && assetsFile.header.m_Version < SerializedFileFormatVersion.Unknown_7)
                    {
                        assetsFile.SetVersion(unityVersion);
                    }
                    CheckStrippedVersion(assetsFile);
                    assetsFileList.Add(assetsFile);
                    assetsFileListHash.Add(reader.FullPath);
                    serializedNameCounts.TryGetValue(assetsFile.fileName,out int nameCount);serializedNameCounts[assetsFile.fileName]=nameCount+1;
                    if (nameCount > 0)
                        Logger.Warning($"Distinct SerializedFiles share name {assetsFile.fileName}; preserving all source identities. External references must resolve unambiguously.");
                }
                catch (Exception e)
                {
                    Logger.Error($"Error while reading assets file {reader.FullPath} from {Path.GetFileName(originalPath)}", e);
                    serializedReadErrors[reader.FullPath] = e.GetBaseException().Message;
                    RegisterResource(reader.FileName, reader, originalPath);
                }
            }
            else
                Logger.Info($"Skipping {originalPath} ({reader.FileName})");
        }

        private void LoadBundleFile(FileReader reader, string originalPath = null, long originalOffset = 0, bool log = true)
        {
            if (log)
            {
                Logger.Info("Loading " + reader.FullPath);
            }
            try
            {
                var bundleFile = new BundleFile(reader, Game);
                foreach (var file in bundleFile.fileList)
                {
                    var dummyPath = Path.Combine(reader.FullPath + ".entries", file.fileName);
                    var subReader = new FileReader(dummyPath, file.stream);
                    if (subReader.FileType == FileType.AssetsFile)
                    {
                        LoadAssetsFromMemory(subReader, originalPath ?? reader.FullPath, bundleFile.m_Header.unityRevision, originalOffset);
                    }
                    else
                    {
                        Logger.Verbose("Caching resource stream");
                        RegisterResource(file.fileName, subReader, originalPath ?? reader.FullPath);
                    }
                }
            }
            catch (InvalidCastException)
            {
                Logger.Error($"Game type mismatch, Expected {nameof(Mr0k)} but got {Game.Name} ({Game.GetType().Name}) !!");
            }
            catch (Exception e)
            {
                var str = $"Error while reading bundle file {reader.FullPath}";
                if (originalPath != null)
                {
                    str += $" from {Path.GetFileName(originalPath)}";
                }
                Logger.Error(str, e);
            }
            finally
            {
                reader.Dispose();
            }
        }

        private void LoadWebFile(FileReader reader)
        {
            Logger.Info("Loading " + reader.FullPath);
            try
            {
                var webFile = new WebFile(reader);
                foreach (var file in webFile.fileList)
                {
                    var dummyPath = Path.Combine(reader.FullPath + ".entries", file.fileName);
                    var subReader = new FileReader(dummyPath, file.stream);
                    switch (subReader.FileType)
                    {
                        case FileType.AssetsFile:
                            LoadAssetsFromMemory(subReader, reader.FullPath);
                            break;
                        case FileType.BundleFile:
                            LoadBundleFile(subReader, reader.FullPath);
                            break;
                        case FileType.WebFile:
                            LoadWebFile(subReader);
                            break;
                        case FileType.ResourceFile:
                            Logger.Verbose("Caching resource stream");
                            RegisterResource(file.fileName, subReader, reader.FullPath);
                            break;
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Error($"Error while reading web file {reader.FullPath}", e);
            }
            finally
            {
                reader.Dispose();
            }
        }

        private void LoadZipFile(FileReader reader)
        {
            Logger.Info("Loading " + reader.FileName);
            try
            {
                using (ZipArchive archive = new ZipArchive(reader.BaseStream, ZipArchiveMode.Read))
                {
                    List<string> splitFiles = new List<string>();
                    Logger.Verbose("Register all files before parsing the assets so that the external references can be found and find split files");
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        if (entry.Name.Contains(".split"))
                        {
                            string baseName = Path.GetFileNameWithoutExtension(entry.Name);
                            string basePath = Path.Combine(Path.GetDirectoryName(entry.FullName), baseName);
                            if (!splitFiles.Contains(basePath))
                            {
                                splitFiles.Add(basePath);
                                importFilesHash.Add(Path.GetFullPath(Path.Combine(reader.FullPath + ".entries", basePath)));
                            }
                        }
                        else
                        {
                            importFilesHash.Add(Path.GetFullPath(Path.Combine(reader.FullPath + ".entries", entry.FullName)));
                        }
                    }

                    Logger.Verbose("Merge split files and load the result");
                    foreach (string basePath in splitFiles)
                    {
                        try
                        {
                            Stream splitStream = new MemoryStream();
                            int i = 0;
                            while (true)
                            {
                                string path = $"{basePath}.split{i++}";
                                ZipArchiveEntry entry = archive.GetEntry(path);
                                if (entry == null)
                                    break;
                                using (Stream entryStream = entry.Open())
                                {
                                    entryStream.CopyTo(splitStream);
                                }
                            }
                            splitStream.Seek(0, SeekOrigin.Begin);
                            FileReader entryReader = new FileReader(basePath, splitStream);
                            entryReader = entryReader.PreProcessing(Game);
                            LoadFile(entryReader);
                        }
                        catch (Exception e)
                        {
                            Logger.Error($"Error while reading zip split file {basePath}", e);
                        }
                    }

                    Logger.Verbose("Load all entries");
                    Logger.Verbose($"Found {archive.Entries.Count} entries"); 
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        try
                        {
                            string dummyPath = Path.Combine(Path.GetDirectoryName(reader.FullPath), reader.FileName, entry.FullName);
                            Logger.Verbose("Create a new stream to store the deflated stream in and keep the data for later extraction");
                            Stream streamReader = new MemoryStream();
                            using (Stream entryStream = entry.Open())
                            {
                                entryStream.CopyTo(streamReader);
                            }
                            streamReader.Position = 0;

                            FileReader entryReader = new FileReader(dummyPath, streamReader);
                            entryReader = entryReader.PreProcessing(Game);
                            LoadFile(entryReader);
                            if (entryReader.FileType == FileType.ResourceFile)
                            {
                                entryReader.Position = 0;
                                Logger.Verbose("Caching resource file");
                                resourceFileReaders.TryAdd(entry.Name, entryReader);
                            }
                        }
                        catch (Exception e)
                        {
                            Logger.Error($"Error while reading zip entry {entry.FullName}", e);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Error($"Error while reading zip file {reader.FileName}", e);
            }
            finally
            {
                reader.Dispose();
            }
        }
        private void LoadBlockFile(FileReader reader)
        {
            Logger.Info("Loading " + reader.FullPath);
            try
            {
                using var stream = new OffsetStream(reader.BaseStream, 0);
                foreach (var offset in stream.GetOffsets(reader.FullPath))
                {
                    var name = offset.ToString("X8");
                    Logger.Info($"Loading Block {name}");

                    var dummyPath = Path.Combine(Path.GetDirectoryName(reader.FullPath), name);
                    var subReader = new FileReader(dummyPath, stream, true);
                    switch (subReader.FileType)
                    {
                        case FileType.ENCRFile:
                        case FileType.BundleFile:
                            LoadBundleFile(subReader, reader.FullPath, offset, false);
                            break;
                        case FileType.BlbFile:
                            LoadBlbFile(subReader, reader.FullPath, offset, false);
                            break;
                        case FileType.MhyFile:
                            LoadMhyFile(subReader, reader.FullPath, offset, false);
                            break;
                    }
                }    
            }
            catch (Exception e)
            {
                Logger.Error($"Error while reading block file {reader.FileName}", e);
            }
            finally
            {
                reader.Dispose();
            }
        }
        private void LoadBlkFile(FileReader reader)
        {
            Logger.Info("Loading " + reader.FullPath);
            try
            {
                using var stream = BlkUtils.Decrypt(reader, (Blk)Game);
                foreach (var offset in stream.GetOffsets(reader.FullPath))
                {
                    var name = offset.ToString("X8");
                    Logger.Info($"Loading Block {name}");

                    var dummyPath = Path.Combine(Path.GetDirectoryName(reader.FullPath), name);
                    var subReader = new FileReader(dummyPath, stream, true);
                    switch (subReader.FileType)
                    {
                        case FileType.BundleFile:
                            LoadBundleFile(subReader, reader.FullPath, offset, false);
                            break;
                        case FileType.MhyFile:
                            LoadMhyFile(subReader, reader.FullPath, offset, false);
                            break;
                    }
                }
            }
            catch (InvalidCastException)
            {
                Logger.Error($"Game type mismatch, Expected {nameof(Blk)} but got {Game.Name} ({Game.GetType().Name}) !!");
            }
            catch (Exception e)
            {
                Logger.Error($"Error while reading blk file {reader.FileName}", e);
            }
            finally
            {
                reader.Dispose();
            }
        }
        private void LoadMhyFile(FileReader reader, string originalPath = null, long originalOffset = 0, bool log = true)
        {
            if (log)
            {
                Logger.Info("Loading " + reader.FullPath);
            }
            try
            {
                var mhyFile = new MhyFile(reader, (Mhy)Game);
                Logger.Verbose($"mhy total size: {mhyFile.m_Header.size:X8}");
                foreach (var file in mhyFile.fileList)
                {
                    var dummyPath = Path.Combine(reader.FullPath + ".entries", file.fileName);
                    var cabReader = new FileReader(dummyPath, file.stream);
                    if (cabReader.FileType == FileType.AssetsFile)
                    {
                        LoadAssetsFromMemory(cabReader, originalPath ?? reader.FullPath, mhyFile.m_Header.unityRevision, originalOffset);
                    }
                    else
                    {
                        Logger.Verbose("Caching resource stream");
                        RegisterResource(file.fileName, cabReader, originalPath ?? reader.FullPath);
                    }
                }
            }
            catch (InvalidCastException)
            {
                Logger.Error($"Game type mismatch, Expected {nameof(Mhy)} but got {Game.Name} ({Game.GetType().Name}) !!");
            }
            catch (Exception e)
            {
                var str = $"Error while reading mhy file {reader.FullPath}";
                if (originalPath != null)
                {
                    str += $" from {Path.GetFileName(originalPath)}";
                }
                Logger.Error(str, e);
            }
            finally
            {
                reader.Dispose();
            }
        }

        private void LoadQtsVFSLegacy(FileReader reader, string originalPath = null, long originalOffset = 0, bool log = true)
        {
            if (log)
            {
                Logger.Info("Loading " + reader.FullPath);
            }

            try
            {
                var qtsVFS = new QtsVFSFile(reader);

                foreach (var entry in qtsVFS.Entries)
                {
                    var fileId = entry.Key;
                    // Zero decoded-length records may contain real index metadata.
                    // Retain every stored byte without guessing its semantic meaning.
                    foreach (var metadata in entry.Value.Where(c => c.UncompressedSize == 0))
                    {
                        reader.Position = metadata.Offset;
                        var raw = reader.ReadBytes(metadata.CompressedSize);
                        if (raw.Length != metadata.CompressedSize) throw new EndOfStreamException("Short metadata record");
                        ContainerEntries.Add(new ContainerEntry(fileId + "-metadata-" + metadata.Offset,
                            reader.FullPath, raw, "QtsRawMetadata", null, metadata.Offset, metadata.UncompressedSize));
                    }
                    var chunks = entry.Value.Where(c => c.UncompressedSize > 0).ToList();
                    if (chunks.Count == 0)
                    {
                        continue;
                    }
                    // Sort by MainBlock and SubBlock to ensure correct file reassembly order
                    chunks.Sort((a, b) =>
                    {
                        int result = a.MainBlock.CompareTo(b.MainBlock);
                        return result != 0 ? result : a.SubBlock.CompareTo(b.SubBlock);
                    });
                    var dummyPath = Path.Combine(reader.FullPath + ".entries", fileId.ToString());
                    try
                    {
                    if (chunks.Count == 1)
                    {
                        var chunk = chunks[0];
                        reader.Position = chunk.Offset;
                        var marker = reader.ReadBytes(Math.Min(chunk.CompressedSize, 12));
                        if (marker.AsSpan().StartsWith("QTSF_PACKAGE"u8))
                        {
                            if (chunk.CompressedSize < chunk.UncompressedSize)
                                throw new InvalidDataException("Raw package record is shorter than its declared marker/payload length");
                            reader.Position = chunk.Offset;
                            var package = reader.ReadBytes(chunk.CompressedSize);
                            if (package.Length != chunk.CompressedSize) throw new EndOfStreamException("Short package metadata");
                            ContainerEntries.Add(new ContainerEntry(fileId.ToString(), reader.FullPath, package,
                                "QtsPackageMetadata", null, chunk.Offset, chunk.UncompressedSize));
                            continue;
                        }
                    }
                    var totalSize = chunks.Sum(c => (long)c.UncompressedSize);
                    if (totalSize <= 0 || totalSize > 512L * 1024 * 1024)
                        throw new InvalidDataException($"QTS entry {fileId} exceeds the 512 MiB decoding limit");
                    var decompressed = new byte[(int)totalSize];
                    var decompressedOffset = 0;
                    foreach (var chunk in chunks)
                    {
                        if (chunk.Offset < 0 || chunk.CompressedSize <= 0 || chunk.CompressedSize > 512 * 1024 * 1024 || chunk.Offset > reader.BaseStream.Length - chunk.CompressedSize)
                            throw new InvalidDataException($"QTS entry {fileId} has an invalid block range");
                        reader.Position = chunk.Offset;
                        var compressed = reader.ReadBytes(chunk.CompressedSize);
                        var decompressedChunk = decompressed.AsSpan(decompressedOffset, chunk.UncompressedSize);

                        int numWrite;
                        if (compressed.Length >= 4 && compressed[0] == 0x28 && compressed[1] == 0xB5 && compressed[2] == 0x2F && compressed[3] == 0xFD)
                        {
                            using var decompressor = new Decompressor();
                            numWrite = decompressor.Unwrap(compressed, 0, compressed.Length, decompressed, decompressedOffset, decompressedChunk.Length);
                        }
                        else if (compressed.Length >= 2 && compressed[0] is 0x8C or 0xCC && compressed[1] is 0x0C)
                        {
                            numWrite = OozHelper.Decompress(compressed, decompressedChunk);
                        }
                        else if (compressed.AsSpan().StartsWith("QTSF_PACKAGE"u8))
                        {
                            // QTS package metadata is stored verbatim even though
                            // it uses the same entry table as compressed payloads.
                            if (compressed.Length != decompressedChunk.Length)
                            {
                                throw new InvalidDataException($"Raw QTS package entry {fileId} has ambiguous mixed-block length");
                            }
                            compressed.AsSpan(0, decompressedChunk.Length).CopyTo(decompressedChunk);
                            numWrite = decompressedChunk.Length;
                        }
                        else
                        {
                            numWrite = LZ4.Instance.Decompress(compressed, decompressedChunk);
                        }

                        if (numWrite != chunk.UncompressedSize)
                            throw new InvalidDataException($"QTS entry {fileId} block wrote {numWrite} bytes; expected {chunk.UncompressedSize}");
                        decompressedOffset += numWrite;
                    }

                    if (decompressedOffset != decompressed.Length)
                    {
                        throw new InvalidDataException($"Failed to decompress {fileId}, expected total {decompressed.Length} bytes but got {decompressedOffset} bytes");
                    }


                    var entryReader = new FileReader(dummyPath, new MemoryStream(decompressed));
                    ContainerEntries.Add(new ContainerEntry(fileId.ToString(), reader.FullPath, decompressed, entryReader.FileType.ToString()));
                    if (entryReader.FileType == FileType.AssetsFile)
                    {
                        LoadAssetsFromMemory(entryReader, reader.FullPath);
                        var loaded = assetsFileList.LastOrDefault(f => f.fullName == dummyPath);
                        if (loaded != null) loaded.containerEntryId = fileId.ToString();
                    }
                    else
                    {
                        // Some QTS entries contain an exact sequence of standalone
                        // SerializedFiles, rather than one file or a resource stream.
                        // Retain the complete original entry and additionally load
                        // validated slices; never scan arbitrary payloads for magic.
                        TryLoadQtsSerializedSequence(decompressed, dummyPath, reader.FullPath);
                        Logger.Verbose("Caching resource stream");
                        RegisterResource(fileId.ToString(), entryReader, reader.FullPath);
                    }
                    }
                    catch (Exception entryError)
                    {
                        Logger.Error($"QTS entry {fileId} could not be decoded; continuing with other entries", entryError);
                        long retained = 0;
                        for (int index = 0; index < chunks.Count; index++)
                        {
                            var chunk = chunks[index];
                            if (chunk.Offset < 0 || chunk.CompressedSize <= 0 || chunk.CompressedSize > 512 * 1024 * 1024 || chunk.Offset > reader.BaseStream.Length - chunk.CompressedSize || retained + chunk.CompressedSize > 512L * 1024 * 1024)
                                continue;
                            reader.Position = chunk.Offset;
                            ContainerEntries.Add(new ContainerEntry(fileId + "-chunk-" + index, reader.FullPath, reader.ReadBytes(chunk.CompressedSize), "CompressedQtsChunk",
                                $"Undecoded source block; entry={fileId}; offset={chunk.Offset}; block={chunk.MainBlock}/{chunk.SubBlock}; expectedBytes={chunk.UncompressedSize}; {entryError.GetBaseException().Message}"));
                            retained += chunk.CompressedSize;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                var str = $"Error while reading QtsVFSFile file {reader.FullPath}";
                if (originalPath != null)
                {
                    str += $" from {Path.GetFileName(originalPath)}";
                }

                Logger.Error(str, e);
            }
            finally
            {
                reader.Dispose();
            }
        }
        
        private bool TryLoadQtsSerializedSequence(byte[] data, string basePath, string originalPath)
        {
            var slices = new List<(int Offset, int Size)>();
            int offset = 0;
            string issue = null;
            while (offset < data.Length)
            {
                if (data.Length - offset < 20 || slices.Count >= 100000) { issue = "short trailing header or slice limit"; break; }
                var header = data.AsSpan(offset);
                uint version = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(header.Slice(8, 4));
                if (version < 9 || version > 22 || header[16] > 1) { issue = "unsupported trailing header"; break; }
                int headerSize = version == 22 ? 48 : 20;
                if (header.Length < headerSize) { issue = "truncated trailing header"; break; }
                uint metadataSize = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(header.Slice(version == 22 ? 20 : 0, 4));
                long size = version == 22 ? System.Buffers.Binary.BinaryPrimitives.ReadInt64BigEndian(header.Slice(24, 8)) : System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(header.Slice(4, 4));
                long dataOffset = version == 22 ? System.Buffers.Binary.BinaryPrimitives.ReadInt64BigEndian(header.Slice(32, 8)) : System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(header.Slice(12, 4));
                if (size < headerSize || size > data.Length - offset || dataOffset < headerSize || dataOffset > size || metadataSize > dataOffset - headerSize)
                { issue = "invalid/truncated trailing SerializedFile range"; break; }
                slices.Add((offset, (int)size));
                offset += (int)size;
            }
            if (slices.Count == 0) return false;
            if (offset != data.Length)
                Logger.Warning($"QTS SerializedFile sequence {basePath}: retained {slices.Count} validated prefix file(s), but {data.Length-offset} trailing bytes at offset {offset} are unparsed ({issue}). Complete original container remains exportable.");
            for (int index = 0; index < slices.Count; index++)
            {
                var slice = slices[index];
                var partData = data.AsSpan(slice.Offset, slice.Size).ToArray();
                var partReader = new FileReader(basePath + "-part-" + index, new MemoryStream(partData));
                if (partReader.FileType != FileType.AssetsFile) { partReader.Dispose(); continue; }
                LoadAssetsFromMemory(partReader, originalPath, originalOffset: slice.Offset);
                var loaded = assetsFileList.LastOrDefault(f => f.fullName == partReader.FullPath);
                if (loaded != null) { loaded.containerEntryId = Path.GetFileName(basePath); loaded.containerByteOffset = slice.Offset; }
            }
            return true;
        }

        private void LoadBlbFile(FileReader reader, string originalPath = null, long originalOffset = 0, bool log = true)
        {
            if (log)
            {
                Logger.Info("Loading " + reader.FullPath);
            }
            try
            {
                var blbFile = new BlbFile(reader, reader.FullPath);
                foreach (var file in blbFile.fileList)
                {
                    var dummyPath = Path.Combine(reader.FullPath + ".entries", file.fileName);
                    var cabReader = new FileReader(dummyPath, file.stream);
                    if (cabReader.FileType == FileType.AssetsFile)
                    {
                        LoadAssetsFromMemory(cabReader, originalPath ?? reader.FullPath, blbFile.m_Header.unityRevision, originalOffset);
                    }
                    else
                    {
                        Logger.Verbose("Caching resource stream");
                        RegisterResource(file.fileName, cabReader, originalPath ?? reader.FullPath);
                    }
                }
            }
            catch (Exception e)
            {
                var str = $"Error while reading Blb file {reader.FullPath}";
                if (originalPath != null)
                {
                    str += $" from {Path.GetFileName(originalPath)}";
                }
                Logger.Error(str, e);
            }
            finally
            {
                reader.Dispose();
            }
        }

        public void CheckStrippedVersion(SerializedFile assetsFile)
        {
            if (assetsFile.IsVersionStripped && string.IsNullOrEmpty(SpecifyUnityVersion))
            {
                throw new Exception("The Unity version has been stripped, please set the version in the options");
            }
            if (!string.IsNullOrEmpty(SpecifyUnityVersion))
            {
                assetsFile.SetVersion(SpecifyUnityVersion);
            }
        }

        public void Clear()
        {
            Logger.Verbose("Cleaning up...");

            foreach (var assetsFile in assetsFileList)
            {
                assetsFile.Objects.Clear();
                assetsFile.reader.Close();
            }
            assetsFileList.Clear();

            foreach (var resourceFileReader in resourceFileReaders)
            {
                resourceFileReader.Value.Close();
            }
            foreach (var resource in scopedResourceReaders.Values) resource.Close();
            foreach (var resource in diskResourceReaders.Values) resource.Close();
            foreach (var resource in retiredResourceReaders) resource.Close();
            diskResourceReaders.Clear();
            resolvedExternals.Clear();
            ClearReferenceIndex();
            retiredResourceReaders.Clear();
            ambiguousScopedResources.Clear();
            scopedResourceReaders.Clear();
            ambiguousResourceNames.Clear();
            resourceFileReaders.Clear();
            ContainerEntries.Clear();
            ClearQts();

            assetsFileIndexCache.Clear();

            tokenSource.Dispose();
            tokenSource = new CancellationTokenSource();

            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        public bool DeferHeavyObjects { get; set; }
        private void ReadAssets()
        {
            Logger.Info("Read assets...");

            var progressCount = assetsFileList.Sum(x => x.m_Objects.Count);
            int i = 0;
            Progress.Reset();
            foreach (var assetsFile in assetsFileList)
            {
                foreach (var objectInfo in assetsFile.m_Objects)
                {
                    if (tokenSource.IsCancellationRequested)
                    {
                        Logger.Info("Reading assets has been cancelled !!");
                        return;
                    }
                    ObjectReader objectReader = null;
                    var status = new ObjectParseStatus { Status = "parser-failed", RemainingBytes = objectInfo.byteSize };
                    assetsFile.ParseStatuses[objectInfo.m_PathID] = status;
                    try
                    {
                        objectReader = new ObjectReader(assetsFile.reader, assetsFile, objectInfo, Game);
                        Object obj = DeferHeavyObjects && objectReader.type.CanParse() && DeferredObject.CanDefer(objectReader.type)
                            ? new DeferredObject(objectReader) : objectReader.type switch
                        {
                            ClassIDType.Animation when ClassIDType.Animation.CanParse() => new Animation(objectReader),
                            ClassIDType.AnimationClip when ClassIDType.AnimationClip.CanParse() => new AnimationClip(objectReader),
                            ClassIDType.Animator when ClassIDType.Animator.CanParse() => new Animator(objectReader),
                            ClassIDType.AnimatorController when ClassIDType.AnimatorController.CanParse() => new AnimatorController(objectReader),
                            ClassIDType.AnimatorOverrideController when ClassIDType.AnimatorOverrideController.CanParse() => new AnimatorOverrideController(objectReader),
                            ClassIDType.AssetBundle when ClassIDType.AssetBundle.CanParse() => new AssetBundle(objectReader),
                            ClassIDType.AudioClip when ClassIDType.AudioClip.CanParse() => new AudioClip(objectReader),
                            ClassIDType.Avatar when ClassIDType.Avatar.CanParse() => new Avatar(objectReader),
                            ClassIDType.Cubemap when ClassIDType.Cubemap.CanParse() => new Cubemap(objectReader),
                            ClassIDType.Font when ClassIDType.Font.CanParse() => new Font(objectReader),
                            ClassIDType.GameObject when ClassIDType.GameObject.CanParse() => new GameObject(objectReader),
                            ClassIDType.IndexObject when ClassIDType.IndexObject.CanParse() => new IndexObject(objectReader),
                            ClassIDType.Material when ClassIDType.Material.CanParse() => new Material(objectReader),
                            ClassIDType.Mesh when ClassIDType.Mesh.CanParse() => new Mesh(objectReader),
                            ClassIDType.MeshFilter when ClassIDType.MeshFilter.CanParse() => new MeshFilter(objectReader),
                            ClassIDType.MeshRenderer when ClassIDType.MeshRenderer.CanParse() => new MeshRenderer(objectReader),
                            ClassIDType.MiHoYoBinData when ClassIDType.MiHoYoBinData.CanParse() => new MiHoYoBinData(objectReader),
                            ClassIDType.MonoBehaviour when ClassIDType.MonoBehaviour.CanParse() => new MonoBehaviour(objectReader),
                            ClassIDType.MonoScript when ClassIDType.MonoScript.CanParse() => new MonoScript(objectReader),
                            ClassIDType.MovieTexture when ClassIDType.MovieTexture.CanParse() => new MovieTexture(objectReader),
                            ClassIDType.PlayerSettings when ClassIDType.PlayerSettings.CanParse() => new PlayerSettings(objectReader),
                            ClassIDType.RectTransform when ClassIDType.RectTransform.CanParse() => new RectTransform(objectReader),
                            ClassIDType.Shader when ClassIDType.Shader.CanParse() => new Shader(objectReader),
                            ClassIDType.ShaderVariantCollection when ClassIDType.ShaderVariantCollection.CanParse() => new ShaderVariantCollection(objectReader),
                            ClassIDType.SkinnedMeshRenderer when ClassIDType.SkinnedMeshRenderer.CanParse() => new SkinnedMeshRenderer(objectReader),
                            ClassIDType.Sprite when ClassIDType.Sprite.CanParse() => new Sprite(objectReader),
                            ClassIDType.SpriteAtlas when ClassIDType.SpriteAtlas.CanParse() => new SpriteAtlas(objectReader),
                            ClassIDType.TextAsset when ClassIDType.TextAsset.CanParse() => new TextAsset(objectReader),
                            ClassIDType.Texture2D when ClassIDType.Texture2D.CanParse() => new Texture2D(objectReader),
                            ClassIDType.Transform when ClassIDType.Transform.CanParse() => new Transform(objectReader),
                            ClassIDType.VideoClip when ClassIDType.VideoClip.CanParse() => new VideoClip(objectReader),
                            ClassIDType.ResourceManager when ClassIDType.ResourceManager.CanParse() => new ResourceManager(objectReader),
                            ClassIDType.ResourceVolumeContext when HokResourceVolumeContext.Supports(objectReader) => new HokResourceVolumeContext(objectReader),
                            _ => new Object(objectReader),
                        };
                        status.Parser = obj.GetType().Name;
                        status.Typed = obj.GetType() != typeof(Object);
                        status.ConsumedBytes = objectReader.Position - objectInfo.byteStart;
                        status.RemainingBytes = objectInfo.byteSize - status.ConsumedBytes;
                        if (status.RemainingBytes < 0) throw new InvalidDataException("Parser crossed the object boundary");
                        status.Status = !status.Typed ? (objectReader.type.CanParse() ? "generic-raw" : "parser-disabled")
                            : status.RemainingBytes == 0 ? "typed-complete" : "typed-partial";
                        if (obj is DeferredObject) { status.Status = "deferred"; status.Typed = false; }
                        if (obj is not DeferredObject && status.Status != "typed-complete" && obj.serializedType != null && obj.serializedType.m_Type == null)
                            obj.serializedType.m_Type = HokTypeSchemas.Find(objectReader);
                        if (obj is not DeferredObject && status.Status != "typed-complete" && obj.serializedType?.m_Type?.m_Nodes?.Count > 1)
                        {
                            long typedPosition = objectReader.Position;
                            try
                            {
                                // The file's own schema is stronger evidence than
                                // a guessed engine-version layout. Do not drop
                                // particle/custom fields just because no hand-written
                                // class exists. Validate all bytes before marking it.
                                var data = obj.ToType();
                                long consumed = objectReader.Position - objectInfo.byteStart;
                                if (consumed != objectInfo.byteSize)
                                    throw new InvalidDataException($"Type tree read {consumed}/{objectInfo.byteSize} bytes");
                                obj.UseTypeTree = true;
                                if (data.Contains("m_Name") && data["m_Name"] is string schemaName) obj.SchemaName = schemaName;
                                status.Parser = "TypeTree/" + ObjectParseStatus.ClassName(objectInfo.classID);
                                status.Typed = true; status.Status = "typed-complete";
                                status.ConsumedBytes = consumed; status.RemainingBytes = 0;
                            }
                            catch (Exception treeError)
                            {
                                status.Error = "Type tree: " + treeError.GetBaseException().Message;
                                objectReader.Position = typedPosition;
                            }
                        }
                        assetsFile.AddObject(obj);
                    }
                    catch (Exception e)
                    {
                        status.Status = "parser-failed";
                        status.Typed = false;
                        status.Error = e.GetBaseException().Message;
                        status.ConsumedBytes = objectReader == null ? 0 : objectReader.Position - objectInfo.byteStart;
                        status.RemainingBytes = objectInfo.byteSize - status.ConsumedBytes;
                        var sb = new StringBuilder();
                        sb.AppendLine("Unable to load object")
                            .AppendLine($"Assets {assetsFile.fileName}")
                            .AppendLine($"Path {assetsFile.originalPath}")
                            .AppendLine($"Type {ObjectParseStatus.ClassName(objectInfo.classID)}")
                            .AppendLine($"PathID {objectInfo.m_PathID}")
                            .AppendLine($"ByteStart 0x{objectInfo.byteStart:X8}")
                            .AppendLine($"CurrentPosition 0x{assetsFile.reader.Position:X8}")
                            .AppendLine($"ObjectOffset 0x{status.ConsumedBytes:X8}")
                            .Append(e);
                        Logger.Error(sb.ToString());
                    }

                    Progress.Report(++i, progressCount);
                }
            }
        }

        private void ProcessAssets()
        {
            Logger.Info("Process Assets...");

            foreach (var assetsFile in assetsFileList)
            {
                foreach (var obj in assetsFile.Objects)
                {
                    if (tokenSource.IsCancellationRequested)
                    {
                        Logger.Info("Processing assets has been cancelled !!");
                        return;
                    }
                    if (obj is GameObject m_GameObject)
                    {
                        Logger.Verbose($"GameObject with {m_GameObject.m_PathID} in file {m_GameObject.assetsFile.fileName} has {m_GameObject.m_Components.Count} components, Attempting to fetch them...");
                        foreach (var pptr in m_GameObject.m_Components)
                        {
                            if (pptr.TryGet(out var m_Component))
                            {
                                switch (m_Component)
                                {
                                    case Transform m_Transform:
                                        Logger.Verbose($"Fetched Transform component with {m_Transform.m_PathID} in file {m_Transform.assetsFile.fileName}, assigning to GameObject components...");
                                        m_GameObject.m_Transform = m_Transform;
                                            break;
                                    case MeshRenderer m_MeshRenderer:
                                        Logger.Verbose($"Fetched MeshRenderer component with {m_MeshRenderer.m_PathID} in file {m_MeshRenderer.assetsFile.fileName}, assigning to GameObject components...");
                                        m_GameObject.m_MeshRenderer = m_MeshRenderer;
                                            break;
                                    case MeshFilter m_MeshFilter:
                                        Logger.Verbose($"Fetched MeshFilter component with {m_MeshFilter.m_PathID} in file {m_MeshFilter.assetsFile.fileName}, assigning to GameObject components...");
                                        m_GameObject.m_MeshFilter = m_MeshFilter;
                                            break;
                                    case SkinnedMeshRenderer m_SkinnedMeshRenderer:
                                        Logger.Verbose($"Fetched SkinnedMeshRenderer component with {m_SkinnedMeshRenderer.m_PathID} in file {m_SkinnedMeshRenderer.assetsFile.fileName}, assigning to GameObject components...");
                                        m_GameObject.m_SkinnedMeshRenderer = m_SkinnedMeshRenderer;
                                            break;
                                    case Animator m_Animator:
                                        Logger.Verbose($"Fetched Animator component with {m_Animator.m_PathID} in file {m_Animator.assetsFile.fileName}, assigning to GameObject components...");
                                        m_GameObject.m_Animator = m_Animator;
                                            break;
                                    case Animation m_Animation:
                                        Logger.Verbose($"Fetched Animation component with {m_Animation.m_PathID} in file {m_Animation.assetsFile.fileName}, assigning to GameObject components...");
                                        m_GameObject.m_Animation = m_Animation;
                                            break;
                                }
                            }
                        }
                    }
                    else if (obj is SpriteAtlas m_SpriteAtlas)
                    {
                        if (m_SpriteAtlas.m_RenderDataMap.Count > 0)
                        {
                            Logger.Verbose($"SpriteAtlas with {m_SpriteAtlas.m_PathID} in file {m_SpriteAtlas.assetsFile.fileName} has {m_SpriteAtlas.m_PackedSprites.Count} packed sprites, Attempting to fetch them...");
                            foreach (var m_PackedSprite in m_SpriteAtlas.m_PackedSprites)
                            {
                                if (m_PackedSprite.TryGet(out var m_Sprite))
                                {
                                    if (m_Sprite.m_SpriteAtlas.IsNull)
                                    {
                                        Logger.Verbose($"Fetched Sprite with {m_Sprite.m_PathID} in file {m_Sprite.assetsFile.fileName}, assigning to parent SpriteAtlas...");
                                        m_Sprite.m_SpriteAtlas.Set(m_SpriteAtlas);
                                    }
                                    else
                                    {
                                        m_Sprite.m_SpriteAtlas.TryGet(out var m_SpriteAtlaOld);
                                        if (m_SpriteAtlaOld.m_IsVariant)
                                        {
                                            Logger.Verbose($"Fetched Sprite with {m_Sprite.m_PathID} in file {m_Sprite.assetsFile.fileName} has a variant of the origianl SpriteAtlas, disposing of the variant and assinging to the parent SpriteAtlas...");
                                            m_Sprite.m_SpriteAtlas.Set(m_SpriteAtlas);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
