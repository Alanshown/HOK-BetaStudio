namespace Hok.Desktop;
internal static class OwnedCache
{
    public static long Clear(string cache,string parent)
    {
        string root=Path.GetFullPath(cache).TrimEnd(Path.DirectorySeparatorChar);
        string expected=Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        if(!root.StartsWith(expected,StringComparison.OrdinalIgnoreCase)||Path.GetDirectoryName(root)+Path.DirectorySeparatorChar!=expected||!Guid.TryParseExact(Path.GetFileName(root),"N",out _))
            throw new IOException("Refusing to clean a non-session cache path.");
        if(!Directory.Exists(root))return 0;
        return ClearDirectory(root);
    }
    static long ClearDirectory(string directory)
    {
        if(File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint))throw new IOException("Cache contains a reparse point.");
        long bytes=0;
        foreach(var entry in Directory.EnumerateFileSystemEntries(directory)){
            var flags=File.GetAttributes(entry);
            if(flags.HasFlag(FileAttributes.ReparsePoint))throw new IOException("Cache contains a reparse point.");
            if(flags.HasFlag(FileAttributes.Directory)){bytes+=ClearDirectory(entry);Directory.Delete(entry);}
            else{bytes+=new FileInfo(entry).Length;File.Delete(entry);}
        }
        return bytes;
    }
}
