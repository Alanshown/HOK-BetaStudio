using System.Text;
using System.Text.RegularExpressions;
namespace Hok.Contracts;
public sealed record DbIdentity(string Stem,string HeroId,string SkinId,string? Shard);
public static partial class Identity {
 [GeneratedRegex(@"^(?<stem>[0-9]{5,})(?:_(?<shard>[0-9]+))?(?<ext>\.db)?$",RegexOptions.IgnoreCase)] private static partial Regex Pattern();
 public static DbIdentity? Parse(string filename,bool signatureVerified=false) {
  var m=Pattern().Match(Path.GetFileName(filename));if(!m.Success||(!m.Groups["ext"].Success&&!signatureVerified))return null;
  var stem=m.Groups["stem"].Value;var code=stem[^5..];return new(stem,code[..3],code,m.Groups["shard"].Success?m.Groups["shard"].Value:null);
 }
 public static string Placeholder(string skinId) {uint hash=2166136261;foreach(var b in Encoding.UTF8.GetBytes(skinId))hash=unchecked((hash^b)*16777619);return new[]{"skin-jade-lotus","skin-tidal-ring","skin-cloud-feather","skin-faceted-seal"}[hash%4];}
 public static string SafeName(string name) {var bad=Path.GetInvalidFileNameChars();var text=new string(name.Select(c=>bad.Contains(c)?'_':c).ToArray()).Trim().TrimEnd('.');if(text.Length>100)text=text[..100];if(string.IsNullOrWhiteSpace(text))text="asset";return text;}
}
public sealed record DbRecord(string Id,string Path,string Name,string Root,string HeroId,string SkinId,string Stem,string? Shard,long Bytes,string Fingerprint,string Signature);
public sealed record ScanResult(List<DbRecord> Files,List<string> Errors,int Visited);
public static class Scanner {
 public static ScanResult Scan(IEnumerable<string> inputs,CancellationToken cancel) {
  var records=new List<DbRecord>();var errors=new List<string>();var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);int visited=0;
  foreach(var input in inputs){cancel.ThrowIfCancellationRequested();var full=Path.GetFullPath(input);var root=Directory.Exists(full)?full:Path.GetDirectoryName(full)!;
   var todo=new Stack<string>();todo.Push(full);
   while(todo.TryPop(out var p)){cancel.ThrowIfCancellationRequested();
    try{var at=File.GetAttributes(p);if(at.HasFlag(FileAttributes.ReparsePoint))continue;
     if(at.HasFlag(FileAttributes.Directory)){foreach(var child in Directory.EnumerateFileSystemEntries(p))todo.Push(child);continue;}
     visited++;if(!seen.Add(p))continue;var name=Path.GetFileName(p);
     if(!Regex.IsMatch(name,@"^\d{5,}(?:_\d+)?(?:\.db)?$",RegexOptions.IgnoreCase))continue;
     var signature=Detect(p);var id=Identity.Parse(name,signature!="unknown");if(id is null)continue;
     var fi=new FileInfo(p);var key=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(p.ToUpperInvariant())))[..24];
     records.Add(new(key,p,name,root,id.HeroId,id.SkinId,id.Stem,id.Shard,fi.Length,$"{fi.Length}:{fi.LastWriteTimeUtc.Ticks}",signature));
    }catch(Exception e)when(e is IOException or UnauthorizedAccessException){errors.Add(p+": "+e.Message);}
   }
  }return new(records.OrderBy(f=>f.HeroId).ThenBy(f=>f.SkinId).ThenBy(f=>f.Name).ToList(),errors,visited);
 }
 public static string Detect(string path){using var f=File.OpenRead(path);Span<byte>b=stackalloc byte[8];var n=f.Read(b);if(n==8&&b.SequenceEqual(new byte[]{1,0,0,2,1,2,3,4}))return "QTS VFS";if(n>=7&&Encoding.ASCII.GetString(b[..7])=="UnityFS")return "UnityFS";return "unknown";}
}

