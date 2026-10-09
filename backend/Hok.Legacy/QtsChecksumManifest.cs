using System;
using System.Collections.Generic;
using System.Text;

namespace AssetStudio;

public static class QtsChecksumManifest
{
    // A same compressed/uncompressed length alone is NOT proof of raw storage.
    // Accept only the complete four-record checksum manifest seen in HOK DBs.
    public static bool TryParse(ReadOnlySpan<byte> data,out Dictionary<string,string> entries)
    {
        entries=null;
        if(data.Length==0||data.Length>16384)return false;
        foreach(var b in data)if(b>127)return false;
        var text=Encoding.ASCII.GetString(data).Replace("\r\n","\n");
        if(text.EndsWith('\n'))text=text[..^1];
        var lines=text.Split('\n');if(lines.Length!=4)return false;
        var parsed=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var line in lines)
        {
            int space=line.IndexOf(' ');if(space<1||line.Length!=space+11||line.Substring(space+1,2)!="0x")return false;
            string name=line[..space],hex=line[(space+3)..];
            if(name is not ("TTre.db" or "ResEntriesDB.db" or "ResScriptDependenciesDB.db" or "BlobDB.db"))return false;
            foreach(char c in hex)if(!Uri.IsHexDigit(c))return false;
            if(!parsed.TryAdd(name,"0x"+hex.ToUpperInvariant()))return false;
        }
        entries=parsed;return true;
    }
}
