using System;
namespace AssetStudio;
// References the already decoded buffer. The desktop does not duplicate source DBs.
public sealed class ContainerEntry
{
    public string Id { get; }
    public string Source { get; }
    readonly byte[] bytes;
    public QtsEntryRecord QtsEntry { get; }
    public byte[] Data => QtsEntry?.Read() ?? bytes;
    public System.IO.Stream Open() => QtsEntry?.Open() ?? new System.IO.MemoryStream(bytes, false);
    public long Length => QtsEntry?.PayloadBytes ?? bytes.Length;
    public byte[] Head(int count=256)=>QtsEntry?.Head(count)??bytes.AsSpan(0,System.Math.Min(count,bytes.Length)).ToArray();
    public void Export(string path){if(QtsEntry!=null)QtsEntry.Export(path);else System.IO.File.WriteAllBytes(path,bytes);}
    public string Kind { get; }
    public string Warning { get; }
    public long? SourceOffset { get; }
    public int? DeclaredDecodedBytes { get; }
    public ContainerEntry(string id, string source, byte[] data, string kind, string warning = null, long? sourceOffset = null, int? declaredDecodedBytes = null)
    { Id=id; Source=source; bytes=data; Kind=kind; Warning=warning; SourceOffset=sourceOffset; DeclaredDecodedBytes=declaredDecodedBytes; }
    public ContainerEntry(string id,string source,QtsEntryRecord entry,string kind,string warning=null){Id=id;Source=source;QtsEntry=entry;Kind=kind;Warning=warning;}
}
