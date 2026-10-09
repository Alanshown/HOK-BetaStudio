using System;
namespace AssetStudio;
// References the already decoded buffer. The desktop does not duplicate source DBs.
public sealed class ContainerEntry
{
    public string Id { get; }
    public string Source { get; }
    readonly byte[] bytes;
    readonly Func<System.IO.Stream> open;readonly long? length;
    readonly byte[] prefix;
    public string DisplayName {get;}
    public QtsEntryRecord QtsEntry { get; }
    public byte[] Data {get{if(open==null)return QtsEntry?.Read()??bytes;using var stream=Open();var result=new byte[checked((int)Length)];stream.ReadExactly(result);return result;}}
    public System.IO.Stream Open() => open?.Invoke()??QtsEntry?.Open() ?? new System.IO.MemoryStream(bytes, false);
    public long Length => length??QtsEntry?.PayloadBytes ?? bytes.Length;
    public byte[] Head(int count=256){if(prefix!=null&&Warning!=null)return prefix.AsSpan(0,Math.Min(count,prefix.Length)).ToArray();if(prefix!=null&&Math.Min(count,Length)<=prefix.Length)return prefix.AsSpan(0,(int)Math.Min(count,Length)).ToArray();if(open==null)return QtsEntry?.Head(count)??bytes.AsSpan(0,System.Math.Min(count,bytes.Length)).ToArray();using var stream=Open();var result=new byte[(int)Math.Min(count,Length)];stream.ReadExactly(result);return result;}
    public void Export(string path){using var input=Open();using var output=System.IO.File.Create(path);input.CopyTo(output);}
    public string Kind { get; }
    public string Warning { get; }
    public long? SourceOffset { get; }
    public int? DeclaredDecodedBytes { get; }
    public ContainerEntry(string id, string source, byte[] data, string kind, string warning = null, long? sourceOffset = null, int? declaredDecodedBytes = null)
    { Id=id; Source=source; bytes=data; Kind=kind; Warning=warning; SourceOffset=sourceOffset; DeclaredDecodedBytes=declaredDecodedBytes; }
    public ContainerEntry(string id,string source,QtsEntryRecord entry,string kind,string warning=null){Id=id;Source=source;QtsEntry=entry;Kind=kind;Warning=warning;}
    public ContainerEntry(string id,string source,Func<System.IO.Stream> open,long length,string kind,string displayName=null,byte[] prefix=null,string warning=null){Id=id;Source=source;this.open=open;this.length=length;Kind=kind;DisplayName=displayName;this.prefix=prefix;Warning=warning;}
}
