using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Buffers.Binary;
using ZstdSharp;

namespace AssetStudio;

// One append-only cache per load. Segments share a handle rather than opening
// hundreds of thousands of files, and have independent bounded positions.
public sealed class QtsPayloadStore : IDisposable
{
    readonly FileStream file;
    public QtsPayloadStore(string directory) { Directory.CreateDirectory(directory);file=new FileStream(Path.Combine(directory,"qts-"+Guid.NewGuid().ToString("N")+".cache"),FileMode.CreateNew,FileAccess.ReadWrite,FileShare.Read,65536,FileOptions.DeleteOnClose|FileOptions.RandomAccess); }
    public long Append(byte[] bytes){lock(file){long at=file.Length;file.Position=at;file.Write(bytes);return at;}}
    public Stream Open(long at,long count)=>new Segment(this,at,count);
    public byte[] Read(long at,int count){var b=new byte[count];using var s=Open(at,count);s.ReadExactly(b);return b;}
    public void Dispose()=>file.Dispose();
    sealed class Segment : Stream {
        readonly QtsPayloadStore store;readonly long offset,length;long position;
        public Segment(QtsPayloadStore store,long offset,long length){this.store=store;this.offset=offset;this.length=length;}
        public override bool CanRead=>true;public override bool CanSeek=>true;public override bool CanWrite=>false;public override long Length=>length;
        public override long Position{get=>position;set{if(value<0)throw new IOException("Negative seek");position=value;}}
        public override int Read(byte[] b,int start,int count)=>Read(b.AsSpan(start,count));
        public override int Read(Span<byte> b){if(position>=length)return 0;lock(store.file){store.file.Position=offset+position;int n=store.file.Read(b[..(int)Math.Min(b.Length,length-position)]);position+=n;return n;}}
        public override long Seek(long n,SeekOrigin origin){Position=(origin==SeekOrigin.Begin?0:origin==SeekOrigin.Current?position:length)+n;return position;}
        public override void Flush(){}public override void SetLength(long n)=>throw new NotSupportedException();public override void Write(byte[] b,int o,int n)=>throw new NotSupportedException();
    }
}
public sealed class QtsChunkState {
    public QtsVFSFile.FHoKCompressedChunk Chunk;
    public long ExpectedBytes,RecoveredBytes;
    public string Codec,Error;
    public long RecoveredOffset;
}
public sealed class QtsEntryRecord {
    public string Source;public ulong FileId;public QtsChunkState[] Chunks;
    public long PayloadOffset,PayloadBytes;public bool Complete;
    public string Kind="UnknownBinary",ParseStatus="not-attempted",Error;
    public string AssetTypes,MetadataName;public int ObjectCount;
    internal Action Recover;
    internal QtsPayloadStore Store;
    string hash;
    public long ExpectedBytes=>Chunks.Sum(c=>c.ExpectedBytes);
    public long CompressedBytes=>Chunks.Sum(c=>(long)c.Chunk.CompressedSize);
    public string Hash { get {if(!Complete)return null;if(hash is null){using var stream=Open();hash=Convert.ToHexString(SHA256.HashData(stream));}return hash;} }
    public Stream Open()=>Store.Open(PayloadOffset,PayloadBytes);
    public byte[] Head(int count=64)=>Store.Read(PayloadOffset,(int)Math.Min(count,PayloadBytes));
    public byte[] Read(){if(PayloadBytes>int.MaxValue)throw new InvalidDataException("Use streaming raw export for this entry");return Store.Read(PayloadOffset,(int)PayloadBytes);}
    public void Export(string path){using var input=Open();using var output=File.Create(path);input.CopyTo(output);}
}
public sealed class QtsDatabaseRecord {
    public string Source;public long Size;public QtsVFSFile Index;public List<QtsEntryRecord> Entries=new();
}
public partial class AssetsManager
{
    public string QtsCacheDirectory;
    QtsPayloadStore qtsStore;
    public List<QtsDatabaseRecord> QtsDatabases {get;}=new();
    readonly Dictionary<string,HashSet<ulong>> qtsEntrySelections=new(StringComparer.OrdinalIgnoreCase);
    public bool QtsLoadIsScoped=>qtsEntrySelections.Count>0;
    // Explicit export selections only. Normal desktop/discovery loads omit this
    // and still enumerate every payload. Nonselected entries retain their index
    // and can supply texture/audio streams on demand, without decoding objects.
    public void SetQtsEntrySelection(string source,IEnumerable<ulong> entries)=>qtsEntrySelections[Path.GetFullPath(source)]=entries.ToHashSet();
    public Dictionary<ulong,List<QtsEntryRecord>> GlobalQtsVFSIndex {get;}=new();
    readonly Dictionary<string,int> serializedNameCounts=new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string,string> serializedReadErrors=new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<QtsEntryRecord,BinaryReader> openedQtsResources=new();
    string[] qtsDependencyPaths=Array.Empty<string>();
    readonly HashSet<string> indexedDependencySources=new(StringComparer.OrdinalIgnoreCase);
    bool dependencyCatalogChecked;QtsRoutingCatalog dependencyCatalog;
    public List<string> QtsReferenceDiagnostics {get;}=new();
    public void SetQtsDependencySources(IEnumerable<string> sources)=>qtsDependencyPaths=sources.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    void IndexQtsDependencies(ulong requestedId,bool allSupplied=false){
        if(!dependencyCatalogChecked)
        {
            dependencyCatalogChecked=true;
            foreach(var source in qtsDependencyPaths.Where(p=>string.Equals(Path.GetFileName(p),"0.db",StringComparison.OrdinalIgnoreCase)))
            {try{
                using var file=new FileReader(source);if(file.FileType!=FileType.QtsVFSFile)continue;var index=new QtsVFSFile(file);
                var catalog=index.Entries.Values.SelectMany(c=>c).Where(c=>c.UncompressedSize==QtsRoutingCatalog.Magic).ToArray();
                if(catalog.Length!=1)continue;var chunk=catalog[0];if(chunk.CompressedSize<52||chunk.CompressedSize>512*1024*1024-4)continue;
                file.Position=chunk.Offset-4;dependencyCatalog=QtsRoutingCatalog.Parse(file.ReadBytes(chunk.CompressedSize+4));break;
            }catch(Exception e){QtsReferenceDiagnostics.Add("Dependency catalog "+source+": "+e.Message);}}
        }
        var route=allSupplied?null:dependencyCatalog?.Resolve(requestedId);
        var candidates=route is null?qtsDependencyPaths:qtsDependencyPaths.Where(p=>string.Equals(Path.GetFileName(Path.GetDirectoryName(p)),route.PackageName,StringComparison.OrdinalIgnoreCase)).ToArray();
        // A supplied corpus may use renamed directories: fall back to the
        // explicitly supplied files, never search an imagined external folder.
        if(candidates.Length==0)candidates=qtsDependencyPaths;
        var loaded=QtsDatabases.Select(d=>d.Source).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach(var source in candidates.Where(s=>!loaded.Contains(s)&&indexedDependencySources.Add(s))){try{
            using var r=new FileReader(source);if(r.FileType!=FileType.QtsVFSFile)continue;var index=new QtsVFSFile(r);var info=new FileInfo(source);var length=info.Length;var stamp=info.LastWriteTimeUtc;
            foreach(var pair in index.Entries){
                // Stored index records are not streaming content candidates.
                if(pair.Value.All(c=>c.UncompressedSize<=1))continue;
                var entry=new QtsEntryRecord{Source=source,FileId=pair.Key,Kind="IndexedPayload",ParseStatus="indexed",Chunks=pair.Value.OrderBy(c=>c.MainBlock).ThenBy(c=>c.SubBlock).ThenBy(c=>c.Offset).Select(c=>new QtsChunkState{Chunk=c,ExpectedBytes=c.UncompressedSize}).ToArray()};
                entry.Recover=()=>{var current=new FileInfo(source);if(current.Length!=length||current.LastWriteTimeUtc!=stamp)throw new IOException("Indexed source changed: "+source);using var file=new FileReader(source);RecoverQts(entry,file);};
                if(!GlobalQtsVFSIndex.TryGetValue(pair.Key,out var locations))GlobalQtsVFSIndex.Add(pair.Key,locations=new());locations.Add(entry);
            }
            foreach(var issue in index.Issues)QtsReferenceDiagnostics.Add(source+": "+issue);
        }catch(Exception e){QtsReferenceDiagnostics.Add(source+": "+e.Message);}}
    }
    internal bool TryGetGlobalQtsResource(SerializedFile owner,ulong id,out BinaryReader reader){
        reader=null;
        bool localAvailable=GlobalQtsVFSIndex.TryGetValue(id,out var all)&&all.Any(e=>e.Complete&&string.Equals(e.Source,owner.originalPath,StringComparison.OrdinalIgnoreCase)&&e.Kind is not ("QtsIndexRecord" or "QtsPackageMetadata"));
        if(!localAvailable)IndexQtsDependencies(id);
        // A catalog may be stale, or its package name may have been reused.
        // Before reporting a missing stream, exhaust only the supplied sources.
        if(!GlobalQtsVFSIndex.TryGetValue(id,out all)||!all.Any(e=>(e.Complete||e.ParseStatus=="indexed")&&e.Kind is not ("QtsIndexRecord" or "QtsPackageMetadata")))
            IndexQtsDependencies(id,allSupplied:true);
        if(!GlobalQtsVFSIndex.TryGetValue(id,out all))return false;
        var candidates=all.Where(e=>(e.Complete||e.ParseStatus=="indexed")&&e.Kind!="QtsIndexRecord"&&e.Kind!="QtsPackageMetadata").ToArray();
        var local=candidates.Where(e=>string.Equals(e.Source,owner.originalPath,StringComparison.OrdinalIgnoreCase)).ToArray();
        if(local.Length>0)candidates=local;
        if(candidates.Length==0)return false;
        foreach(var candidate in candidates)if(!candidate.Complete){candidate.Recover?.Invoke();if(!candidate.Complete)throw new InvalidDataException(candidate.Error);}
        if(candidates.Length>1&&candidates.Select(e=>e.Hash).Distinct().Count()>1)throw new InvalidDataException($"Ambiguous QTS resource {id}: {string.Join(", ",candidates.Select(e=>e.Source))}");
        var selected=candidates[0];if(!openedQtsResources.TryGetValue(selected,out reader)){openedQtsResources.Add(selected,reader=new BinaryReader(selected.Open()));if(selected.Source!=owner.originalPath)QtsReferenceDiagnostics.Add($"Resolved cross-DB {owner.fullName} -> {id} -> {selected.Source}");}return true;
    }
    void ClearQts(){foreach(var r in openedQtsResources.Values)r.Dispose();openedQtsResources.Clear();GlobalQtsVFSIndex.Clear();QtsDatabases.Clear();qtsEntrySelections.Clear();qtsStore?.Dispose();qtsStore=null;serializedNameCounts.Clear();serializedReadErrors.Clear();qtsDependencyPaths=Array.Empty<string>();indexedDependencySources.Clear();dependencyCatalogChecked=false;dependencyCatalog=null;QtsReferenceDiagnostics.Clear();qtsTypeSchemas.Clear();qtsTypeDependenciesChecked=false;}
    private void RecoverQts(QtsEntryRecord entry,FileReader reader)
    {
        qtsStore??=new QtsPayloadStore(QtsCacheDirectory??Path.Combine(Path.GetTempPath(),"hok-qts-cache"));entry.Store=qtsStore;
                entry.Complete=true;entry.PayloadBytes=0;entry.Error=null;bool first=true,metadata=true;
                foreach(var state in entry.Chunks){var chunk=state.Chunk;try{
                    // INGQ is a stored value whose first four bytes occupy the
                    // usual decoded-size word. It must not be interpreted as a
                    // gigabyte allocation or exported with its magic removed.
                    if(chunk.UncompressedSize==QtsRoutingCatalog.Magic)
                    {
                        if(chunk.CompressedSize<52||chunk.CompressedSize>512*1024*1024-4)throw new InvalidDataException("Stored catalog size limit");
                        reader.Position=chunk.Offset-4;var catalog=reader.ReadBytes(chunk.CompressedSize+4);
                        QtsRoutingCatalog.Parse(catalog);metadata=false;state.Codec="StoredRoutingCatalog";state.ExpectedBytes=catalog.Length;
                        state.RecoveredOffset=qtsStore.Append(catalog);state.RecoveredBytes=catalog.Length;
                        if(first){entry.PayloadOffset=state.RecoveredOffset;first=false;}entry.PayloadBytes+=catalog.Length;continue;
                    }
                    if(chunk.CompressedSize>512*1024*1024||chunk.UncompressedSize>512*1024*1024)throw new InvalidDataException("Chunk exceeds 512 MiB safe decode limit; original chunk remains exportable");
                    reader.Position=chunk.Offset;var raw=reader.ReadBytes(chunk.CompressedSize);if(raw.Length!=chunk.CompressedSize)throw new EndOfStreamException("Short compressed chunk");
                    byte[] bytes;
                    // Index records: flags + fileId + logical length + digest +
                    // a counted 12-byte location table. 0/1 is NOT decoded size.
                    bool indexRecord=chunk.UncompressedSize<=1&&raw.Length>=40&&(raw.Length-40)%12==0&&BinaryPrimitives.ReadUInt64LittleEndian(raw.AsSpan(4))==entry.FileId&&BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(36))==(raw.Length-40)/12;
                    if(indexRecord||chunk.UncompressedSize==0){bytes=raw;state.ExpectedBytes=raw.Length;state.Codec="StoredIndex";}
                    else if(raw.AsSpan().StartsWith("QTSF_PACKAGE"u8)){bytes=raw;state.ExpectedBytes=raw.Length;state.Codec="StoredPackage";}
                    else if(raw.Length==chunk.UncompressedSize&&QtsChecksumManifest.TryParse(raw,out _))
                    {bytes=raw;metadata=false;state.Codec="StoredChecksumManifest";}
                    else {
                        metadata=false;bytes=new byte[chunk.UncompressedSize];int n;
                        if(raw.AsSpan().StartsWith(new byte[]{0x28,0xb5,0x2f,0xfd})){state.Codec="Zstd";using var z=new Decompressor();n=z.Unwrap(raw,0,raw.Length,bytes,0,bytes.Length);}
                        else if(raw.Length>=2&&(raw[0]==0x8c||raw[0]==0xcc)&&raw[1]==0x0c){state.Codec="Oodle";n=OozHelper.Decompress(raw,bytes);}
                        else {state.Codec="LZ4";n=LZ4.Instance.Decompress(raw,bytes);}
                        if(n!=bytes.Length)throw new InvalidDataException($"Decoded {n}, expected {bytes.Length}");
                    }
                    state.RecoveredOffset=qtsStore.Append(bytes);state.RecoveredBytes=bytes.Length;if(first){entry.PayloadOffset=state.RecoveredOffset;first=false;}entry.PayloadBytes+=bytes.Length;
                }catch(Exception e){state.Error=e.GetBaseException().Message;entry.Complete=false;}}
                if(!entry.Complete){entry.Kind="UnknownCompressed";entry.ParseStatus="recovery-failed";entry.Error=string.Join("; ",entry.Chunks.Where(c=>c.Error!=null).Select(c=>$"{c.Chunk.Offset}: {c.Error}"));Logger.Error($"QTS {entry.Source}:{entry.FileId}: {entry.Error}");return;}
                if(metadata){entry.Kind=entry.Chunks.All(c=>c.Codec=="StoredPackage")?"QtsPackageMetadata":"QtsIndexRecord";entry.ParseStatus="identified-metadata";return;}
    }
    private void LoadQtsVFS(FileReader reader,string originalPath=null,long originalOffset=0,bool log=true)
    {
        try {
            qtsStore??=new QtsPayloadStore(QtsCacheDirectory??Path.Combine(Path.GetTempPath(),"hok-qts-cache"));
            var index=new QtsVFSFile(reader);var db=new QtsDatabaseRecord{Source=reader.FullPath,Size=reader.Length,Index=index};QtsDatabases.Add(db);
            qtsEntrySelections.TryGetValue(reader.FullPath,out var selectedEntries);
            if(selectedEntries!=null)foreach(var missing in selectedEntries.Where(id=>!index.Entries.ContainsKey(id)))Logger.Error($"Selected QTS entry {missing} is absent from {reader.FullPath}");
            var sourceInfo=new FileInfo(reader.FullPath);string source=reader.FullPath;long sourceBytes=sourceInfo.Length;var sourceStamp=sourceInfo.LastWriteTimeUtc;
            foreach(var issue in index.Issues)Logger.Error(reader.FullPath+": "+issue);
            foreach(var pair in index.Entries){
                var entry=new QtsEntryRecord{Source=reader.FullPath,FileId=pair.Key,Store=qtsStore,Chunks=pair.Value.OrderBy(c=>c.MainBlock).ThenBy(c=>c.SubBlock).ThenBy(c=>c.Offset).Select(c=>new QtsChunkState{Chunk=c,ExpectedBytes=c.UncompressedSize}).ToArray()};
                db.Entries.Add(entry);if(!GlobalQtsVFSIndex.TryGetValue(pair.Key,out var locations))GlobalQtsVFSIndex.Add(pair.Key,locations=new());locations.Add(entry);
                if(selectedEntries!=null&&!selectedEntries.Contains(pair.Key))
                {
                    entry.Kind="IndexedPayload";entry.ParseStatus="indexed";
                    entry.Recover=()=>{var current=new FileInfo(source);if(current.Length!=sourceBytes||current.LastWriteTimeUtc!=sourceStamp)throw new IOException("Indexed source changed: "+source);using var input=new FileReader(source);RecoverQts(entry,input);};
                    continue;
                }
                RecoverQts(entry,reader);if(!entry.Complete||entry.ParseStatus=="identified-metadata")continue;
                string dummyPath=Path.Combine(reader.FullPath+".entries",pair.Key.ToString());
                var before=assetsFileList.Count;FileReader er=null;
                try {
                    er=new FileReader(dummyPath,entry.Open());entry.Kind=er.FileType.ToString();
                    if(er.FileType==FileType.QtsVFSFile)
                    {
                        var kv=QtsKeyValueDatabase.Parse(entry.Read());entry.Kind="QtsKeyValueDatabase";entry.ParseStatus="key-value-table-read";
                        if(QtsTypeTreeDatabase.LooksLike(kv))
                        {
                            var trees=QtsTypeTreeDatabase.Parse(kv);RegisterQtsTypeDatabase(trees,dummyPath);entry.Kind="QtsTypeTreeDatabase";entry.ParseStatus="type-schemas-read";
                        }
                        er.Dispose();
                    }
                    else if(er.FileType==FileType.AssetsFile){LoadAssetsFromMemory(er,reader.FullPath);}
                    else if(er.FileType==FileType.BundleFile){LoadBundleFile(er,reader.FullPath,log:false);}
                    else if(er.FileType==FileType.ZipFile)
                    {
                        var archive=ZipContainerData.ReadIndex(()=>entry.Open());
                        if(archive.IsNumpy)
                        {
                            var arrays=NumpyData.ReadArchive(()=>entry.Open());entry.Kind="NumpyArchive";entry.ParseStatus="array-headers-read";
                            foreach(var array in arrays)ContainerEntries.Add(new ContainerEntry(pair.Key+"/"+array.Name,reader.FullPath,
                                ()=>NumpyData.OpenMember(()=>entry.Open(),array.Name,array.Bytes),array.Bytes,"NumpyArray"));
                        }
                        else
                        {
                            entry.Kind=archive.Kind;entry.ParseStatus="archive-directory-read";
                            foreach(var member in archive.Members.Where(m=>!m.Directory))
                            {
                                ContainerEntries.Add(new ContainerEntry(pair.Key+"/zip/"+member.Index,reader.FullPath,
                                    ()=>ZipContainerData.OpenMember(()=>entry.Open(),member),member.Bytes,"ArchiveMember",member.Name,member.Head,member.Error));
                                if(member.Error!=null){entry.ParseStatus="parser-failed";entry.Error=(entry.Error==null?"":entry.Error+"; ")+member.Name+": "+member.Error;Logger.Error(entry.Error);}
                            }
                        }
                        er.Dispose();
                    }
                    else {
                        // Sequence slicing uses a temporary buffer, never a retained
                        // duplicate. Non-Unity streams stay disk-backed and indexed.
                        var head=entry.Head(20);bool possible=head.Length>=20&&BinaryPrimitives.ReadUInt32BigEndian(head.AsSpan(8)) is >=9 and <=22;
                        if(possible)TryLoadQtsSerializedSequence(entry.Read(),dummyPath,reader.FullPath);
                        er.Dispose();
                    }
                    for(int i=before;i<assetsFileList.Count;i++){var f=assetsFileList[i];f.containerEntryId=pair.Key.ToString();}
                    entry.ObjectCount=assetsFileList.Skip(before).Sum(f=>f.m_Objects.Count);
                    if(assetsFileList.Count>before){entry.ParseStatus="object-table-read";entry.AssetTypes=string.Join(", ",assetsFileList.Skip(before).SelectMany(f=>f.m_Objects).Select(o=>ObjectParseStatus.ClassName(o.classID)).Distinct());}
                    else if(entry.ParseStatus is not ("key-value-table-read" or "type-schemas-read" or "array-headers-read" or "archive-directory-read" or "parser-failed"))entry.ParseStatus=entry.Kind=="ResourceFile"?"unclassified-stream":"parser-failed";
                    var failures=serializedReadErrors.Where(x=>x.Key==dummyPath||x.Key.StartsWith(dummyPath+"-part-",StringComparison.OrdinalIgnoreCase)||x.Key.StartsWith(dummyPath+".entries"+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)).Select(x=>x.Key+": "+x.Value).ToArray();
                    if(failures.Length>0){entry.Error=string.Join("; ",failures);entry.ParseStatus=entry.ObjectCount>0?"object-table-partial":"parser-failed";}
                    if(entry.ParseStatus=="parser-failed"&&entry.Error==null){entry.Error="Recognized "+entry.Kind+" but no supported payload parser completed";Logger.Error(dummyPath+": "+entry.Error);}
                }catch(Exception e){er?.Dispose();entry.ParseStatus="parser-failed";entry.Error=e.GetBaseException().Message;Logger.Error($"QTS payload {pair.Key} retained after parser failure",e);}
                ContainerEntries.Add(new ContainerEntry(pair.Key.ToString(),reader.FullPath,entry,entry.Kind,entry.Error));
            }
            IdentifyQtsMappingMetadata(db);
        }catch(Exception e){Logger.Error("QTS database "+reader.FullPath,e);}finally{reader.Dispose();}
    }
}
