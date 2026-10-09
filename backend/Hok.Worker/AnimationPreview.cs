using AssetStudio;
using Hok.Contracts;
using Obj=AssetStudio.Object;

namespace Hok.Worker;

internal static class AnimationPreview
{
    static bool References(PPtr<AnimationClip>? pointer,AnimationClip clip)=>pointer is not null&&pointer.m_PathID==clip.m_PathID&&pointer.TryGet(out var target)&&ReferenceEquals(target,clip);
    static bool ControllerReferences(RuntimeAnimatorController controller,AnimationClip clip,HashSet<RuntimeAnimatorController> visited)
    {
        if(!visited.Add(controller))return false;
        if(controller is AnimatorController c)return c.m_AnimationClips.Any(p=>References(p,clip));
        if(controller is AnimatorOverrideController o){
            if(o.m_Clips.Any(p=>References(p.m_OverrideClip,clip)))return true;
            if(o.m_Clips.Any(p=>References(p.m_OriginalClip,clip)&&!p.m_OverrideClip.IsNull))return false;
            return o.m_Controller.TryGet(out var parent)&&ControllerReferences(parent,clip,visited);
        }
        return false;
    }
    internal static List<GameObject> FindRoots(AssetsManager manager,AnimationClip clip)
    {
        var roots=new HashSet<GameObject>();
        foreach(var obj in manager.assetsFileList.SelectMany(f=>f.Objects)){
            if(obj is Animation a&&(References(a.m_Animation,clip)||a.m_Animations.Any(p=>References(p,clip)))&&a.m_GameObject.TryGet(out var go))roots.Add(go);
            if(obj is Animator animator&&animator.m_Controller.TryGet(out var controller)&&ControllerReferences(controller,clip,[])&&animator.m_GameObject.TryGet(out var animated))roots.Add(animated);
        }
        // No name-based or whole-package fallback: unrelated rigs must not be
        // paired merely because they contain an Idle/Run clip with the same name.
        return roots.Where(g=>g.m_Transform is not null).ToList();
    }
    internal static GameObject SelectBestRoot(IReadOnlyCollection<GameObject> roots,AnimationClip clip)
    {
        if(roots.Count==0)throw new InvalidDataException("No related animation hierarchy");
        if(roots.Count==1)return roots.First();
        GameObject? best=null;int bestTracks=-1;long bestKeys=-1,bestVertices=-1;
        foreach(var root in roots.OrderBy(g=>g.assetsFile.fullName,StringComparer.Ordinal).ThenBy(g=>g.m_PathID))
        {
            // A clip can be referenced by stripped LOD/effect variants. Picking
            // the first related root (or just the most geometry) may discard
            // every curve even when another explicitly related rig binds it.
            var options=new ModelConverter.Options{game=clip.assetsFile.game,imageFormat=ImageFormat.Png,collectAnimations=false,exportMaterials=false,materials=[],uvs=Enumerable.Range(0,8).ToDictionary(i=>"UV"+i,i=>(true,i)),texs=[]};
            var model=new ModelConverter(root,options,[clip]);
            var bound=model.AnimationList.SelectMany(a=>a.TrackList).Where(t=>!string.IsNullOrEmpty(t.Path)&&model.RootFrame.FindFrameByPath(t.Path)!=null&&t.Rotations.Count+t.Translations.Count+t.Scalings.Count+(t.BlendShape?.Keyframes.Count??0)>0).ToArray();
            long keys=bound.Sum(t=>(long)t.Rotations.Count+t.Translations.Count+t.Scalings.Count+(t.BlendShape?.Keyframes.Count??0));
            long vertices=model.MeshList.Sum(m=>(long)m.VertexList.Count);
            if(bound.Length>bestTracks||bound.Length==bestTracks&&(keys>bestKeys||keys==bestKeys&&vertices>bestVertices))
            {best=root;bestTracks=bound.Length;bestKeys=keys;bestVertices=vertices;}
        }
        return best!;
    }
    static IEnumerable<GameObject> Hierarchy(GameObject root)
    {
        var seen=new HashSet<Transform>();var queue=new Queue<Transform>();queue.Enqueue(root.m_Transform);
        while(queue.TryDequeue(out var t)){
            if(!seen.Add(t))throw new InvalidDataException("Cyclic animation hierarchy");
            if(t.m_GameObject.TryGet(out var go))yield return go;
            foreach(var child in t.m_Children)if(child.TryGet(out var c))queue.Enqueue(c);
        }
    }
    public static object Build(AssetsManager manager,AnimationClip clip,string cache)
    {
        var roots=FindRoots(manager,clip);
        if(roots.Count==0)return AnimationCurves.Preview(clip,cache);
        // Remove nested duplicates already included by an ancestor root.
        var descendants=roots.ToDictionary(r=>r,r=>Hierarchy(r).ToHashSet());
        roots=roots.Where(r=>!roots.Any(other=>other!=r&&descendants[other].Contains(r))).ToList();
        var warnings=new List<string>();
        if(roots.Count>1){
            // A reused clip can reference many LODs/skins; overlaying all of
            // them produces a broken scene and ambiguous duplicate bone names.
            roots=[SelectBestRoot(roots,clip)];
            warnings.Add("variants");
        }
        var related=roots.SelectMany(r=>descendants[r]).Distinct().ToArray();
        int particles=related.SelectMany(g=>g.m_Components).Count(p=>p.Cast<Obj>().TryGet(out var o)&&o.type==ClassIDType.ParticleSystem);
        if(particles>0)warnings.Add("particles");
        if(related.SelectMany(g=>new Renderer?[]{g.m_MeshRenderer,g.m_SkinnedMeshRenderer}).Where(r=>r is not null)
            .Any(r=>r!.m_Materials.Any(m=>!m.IsNull&&!m.TryGet(out _))))warnings.Add("materials");
        var options=new ModelConverter.Options{game=manager.Game,imageFormat=ImageFormat.Png,collectAnimations=false,exportMaterials=true,materials=[],uvs=Enumerable.Range(0,8).ToDictionary(i=>"UV"+i,i=>(true,i)),texs=[]};
        ModelConverter Create()=>roots.Count==1?new ModelConverter(roots[0],options,[clip]):new ModelConverter("HOK Animation",roots,options,[clip]);
        ModelConverter model;
        try{model=Create();}
        catch(IOException){options.exportMaterials=false;model=Create();warnings.Add("materials");}
        if(model.MeshList.Count==0)return AnimationCurves.Preview(clip,cache);
        if(!model.AnimationList.Any(a=>a.TrackList.Any(t=>t.Rotations.Count+t.Translations.Count+t.Scalings.Count>0)))
            throw new InvalidDataException("ANIMATION_NO_SUPPORTED_TRACKS");
        if(clip.m_FloatCurves.Any(c=>c.classID!=ClassIDType.SkinnedMeshRenderer)||clip.m_PPtrCurves.Count>0)warnings.Add("properties");
        string token=Guid.NewGuid().ToString("N");
        var textureNames=model.TextureList.ToDictionary(t=>t.Name,t=>token+"-"+Identity.SafeName(t.Name));
        foreach(var material in model.MaterialList)foreach(var texture in material.Textures)
            if(textureNames.TryGetValue(texture.Name,out var name))texture.Name=name;
        foreach(var texture in model.TextureList)texture.Name=textureNames[texture.Name];
        var file=token+".fbx";var previous=Directory.GetCurrentDirectory();
        try{ModelExporter.ExportFbx(Path.Combine(cache,file),model,new Fbx.ExportOptions{exportAllNodes=true,exportSkins=true,exportAnimations=true,exportBlendShape=true,eulerFilter=true,filterPrecision=.25f,boneSize=10,scaleFactor=1,fbxVersion=3,fbxFormat=0});}
        finally{Directory.SetCurrentDirectory(previous);}
        if(!File.Exists(Path.Combine(cache,file)))throw new IOException("Animation scene export produced no file");
        return new{file,kind="fbx",animation=clip.Name,roots=roots.Select(g=>g.Name).ToArray(),meshes=model.MeshList.Count,textures=model.TextureList.Count,particles,warnings=warnings.Distinct().ToArray()};
    }
}
