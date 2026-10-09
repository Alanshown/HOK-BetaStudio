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
            FileAttributes flags;
            try{flags=File.GetAttributes(entry);}catch(FileNotFoundException){continue;}
            if(flags.HasFlag(FileAttributes.ReparsePoint))throw new IOException("Cache contains a reparse point.");
            if(flags.HasFlag(FileAttributes.Directory)){bytes+=ClearDirectory(entry);Directory.Delete(entry);}
            else{
                try{bytes+=new FileInfo(entry).Length;}catch(FileNotFoundException){continue;}
                // DeleteOnClose/native scanner handles may outlive worker exit
                // briefly. Retry only sharing/lock violations, never unsafe paths.
                for(int attempt=0;;attempt++){
                    try{File.Delete(entry);break;}
                    catch(IOException e) when(attempt<8&&(e.HResult&0xffff) is 32 or 33){Thread.Sleep(25*(attempt+1));}
                }
            }
        }
        return bytes;
    }
}
