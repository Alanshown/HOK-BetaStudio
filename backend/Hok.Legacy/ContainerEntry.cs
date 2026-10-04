namespace AssetStudio;
// References the already decoded buffer. The desktop does not duplicate source DBs.
public sealed class ContainerEntry
{
    public string Id { get; }
    public string Source { get; }
    public byte[] Data { get; }
    public string Kind { get; }
    public string Warning { get; }
    public long? SourceOffset { get; }
    public int? DeclaredDecodedBytes { get; }
    public ContainerEntry(string id, string source, byte[] data, string kind, string warning = null, long? sourceOffset = null, int? declaredDecodedBytes = null)
    { Id=id; Source=source; Data=data; Kind=kind; Warning=warning; SourceOffset=sourceOffset; DeclaredDecodedBytes=declaredDecodedBytes; }
}
