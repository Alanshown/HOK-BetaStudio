using AssetStudio;
using System.Text.Json;
namespace Hok.Worker;

internal static class AnimationCurves
{
 internal sealed record Key(float Time,float[] Value);
 internal sealed record Track(string Path,string Property,int? ClassId,Key[] Keys);
 internal sealed record Document(string Name,float Duration,float SampleRate,List<Track> Tracks,int ObjectReferenceCurves,string Mode="curves");
 public static Document Read(AnimationClip clip){
  _=clip.Convert(); // Converts legacy + dense + streamed curves idempotently.
  var tracks=new List<Track>();
  void Add(string path,string property,int? classId,IEnumerable<Key> keys){var a=keys.ToArray();if(a.Any(k=>!float.IsFinite(k.Time)||k.Value.Any(v=>!float.IsFinite(v))))throw new InvalidDataException("Animation contains non-finite key time/value");if(a.Length>0)tracks.Add(new(path,property,classId,a));}
  foreach(var c in clip.m_PositionCurves)Add(c.path,"position",4,c.curve.m_Curve.Select(k=>new Key(k.time,[k.value.X,k.value.Y,k.value.Z])));
  foreach(var c in clip.m_RotationCurves)Add(c.path,"rotation",4,c.curve.m_Curve.Select(k=>new Key(k.time,[k.value.X,k.value.Y,k.value.Z,k.value.W])));
  foreach(var c in clip.m_ScaleCurves)Add(c.path,"scale",4,c.curve.m_Curve.Select(k=>new Key(k.time,[k.value.X,k.value.Y,k.value.Z])));
  foreach(var c in clip.m_EulerCurves)Add(c.path,"euler",4,c.curve.m_Curve.Select(k=>new Key(k.time,[k.value.X,k.value.Y,k.value.Z])));
  foreach(var c in clip.m_FloatCurves)Add(c.path,c.attribute,(int)c.classID,c.curve.m_Curve.Select(k=>new Key(k.time,[(float)k.value])));
  return new(clip.Name,tracks.SelectMany(t=>t.Keys).Select(k=>k.Time).DefaultIfEmpty(0).Max(),clip.m_SampleRate,tracks,clip.m_PPtrCurves.Count);
 }
 public static object Preview(AnimationClip clip,string cache){var doc=Read(clip);string file=Guid.NewGuid().ToString("N")+".curves.json";File.WriteAllText(Path.Combine(cache,file),JsonSerializer.Serialize(doc,Program.Json));return new{file,kind="curves",animation=clip.Name,warnings=new[]{"no-related-model"}};}
}
