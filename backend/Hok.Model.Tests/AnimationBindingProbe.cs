using AssetStudio;
using Hok.Worker;
using System.Text.Json;

internal static class AnimationBindingProbe
{
 internal static void Run(string[] args)
 {
  string root=Path.GetFullPath(args[0]),sourceFilter=args[1],schema=Path.GetFullPath(args[2]);
  using var graph=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root,"evidence/dependency-graph.json")));
  var nodes=graph.RootElement.GetProperty("nodes").EnumerateArray().Where(n=>Path.GetFileName(n.GetProperty("sourcePath").GetString())==sourceFilter).ToArray();
  string source=nodes.Select(n=>n.GetProperty("sourcePath").GetString()!).Distinct().Single();
  var manager=new AssetsManager{Game=GameManager.GetGame(GameType.HonorOfKings),DeferHeavyObjects=true,QtsCacheDirectory=Path.GetFullPath(".cache/binding-probe")};
  Logger.Silent=true;manager.SetQtsEntrySelection(source,nodes.Select(n=>ulong.Parse(n.GetProperty("entry").GetString()!)));manager.LoadQtsTypeDatabase(schema);
  try
  {
   manager.LoadFilesReadOnly(source);var results=new List<object>();
   foreach(var obj in manager.assetsFileList.SelectMany(f=>f.Objects).Where(o=>o.type==ClassIDType.AnimationClip&&args.Skip(3).Contains(o.m_PathID.ToString())))
   {
    var clip=(AnimationClip)DeferredObject.Resolve(obj);var models=new List<object>();
    foreach(var go in AnimationPreview.FindRoots(manager,clip).OrderBy(g=>g.assetsFile.fullName,StringComparer.Ordinal).ThenBy(g=>g.m_PathID))
    {
     var options=new ModelConverter.Options{game=manager.Game,collectAnimations=false,exportMaterials=false,materials=[],uvs=Enumerable.Range(0,8).ToDictionary(i=>"UV"+i,i=>(true,i)),texs=[]};
     try
     {
      var model=new ModelConverter(go,options,[clip]);var frames=new List<string>();
      void Visit(ImportedFrame frame){frames.Add(frame.Path);for(int i=0;i<frame.Count;i++)Visit(frame[i]);}Visit(model.RootFrame);
      models.Add(new{root=go.Name,pathId=go.m_PathID.ToString(),source=go.assetsFile.fullName,frames,tracks=model.AnimationList.SelectMany(a=>a.TrackList).Select(t=>new{t.Path,bound=!string.IsNullOrEmpty(t.Path)&&model.RootFrame.FindFrameByPath(t.Path)!=null,positionKeys=t.Translations.Count,rotationKeys=t.Rotations.Count,scaleKeys=t.Scalings.Count})});
     }catch(Exception e){models.Add(new{root=go.Name,error=e.ToString()});}
    }
    results.Add(new{name=clip.Name,pathId=clip.m_PathID.ToString(),models});
   }
   string dest=Path.Combine(root,"evidence","animation-binding-probe-"+sourceFilter+".json");File.WriteAllText(dest,JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(new{source,clips=results.Count,report=dest}));
  }
  finally{manager.Clear();}
 }
}
