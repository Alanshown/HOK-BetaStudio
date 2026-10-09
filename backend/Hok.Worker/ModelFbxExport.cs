using AssetStudio;
using Hok.Contracts;
using Obj = AssetStudio.Object;

namespace Hok.Worker;

internal static class ModelFbxExport
{
    static bool References(PPtr<Mesh> pointer, Mesh mesh) => pointer.m_PathID == mesh.m_PathID &&
        pointer.TryGet(out var target) && ReferenceEquals(target, mesh);

    static GameObject Root(GameObject owner)
    {
        var current = owner;
        var seen = new HashSet<Transform>();
        while (current.m_Transform is { } transform)
        {
            if (!seen.Add(transform)) throw new InvalidDataException("Cyclic model hierarchy");
            if (current.m_Animator != null || current.m_Animation != null) return current;
            if (!transform.m_Father.TryGet(out var parent) || !parent.m_GameObject.TryGet(out var go)) break;
            current = go;
        }
        return current;
    }

    internal static List<GameObject> FindMeshRoots(Mesh mesh)
    {
        var owners = new HashSet<GameObject>();
        foreach (var obj in mesh.assetsFile.assetsManager.assetsFileList.SelectMany(f => f.Objects))
        {
            if (obj is SkinnedMeshRenderer skin && References(skin.m_Mesh, mesh) && skin.m_GameObject.TryGet(out var a)) owners.Add(Root(a));
            if (obj is MeshFilter filter && References(filter.m_Mesh, mesh) && filter.m_GameObject.TryGet(out var b) && b.m_MeshRenderer != null) owners.Add(Root(b));
        }
        return owners.OrderByDescending(g => g.m_Animator != null || g.m_Animation != null)
            .ThenBy(g => g.assetsFile.fullName, StringComparer.Ordinal).ThenBy(g => g.m_PathID).ToList();
    }

    internal static string[] Write(Obj obj, string path, ExportOptions settings)
    {
        if (!float.IsFinite(settings.Scale) || settings.Scale <= 0 || settings.Scale > 1000)
            throw new ArgumentOutOfRangeException(nameof(settings.Scale));
        var warnings = new List<string>();
        var options = new ModelConverter.Options { game=obj.assetsFile.game, imageFormat=ImageFormat.Png,
            collectAnimations=settings.Animations, exportMaterials=settings.Materials, materials=[],
            uvs=Enumerable.Range(0,8).ToDictionary(i=>"UV"+i,i=>(settings.AllUv||i==0,i)), texs=[] };
        ModelConverter model;
        ModelConverter Create(Func<ModelConverter> convert)
        {
            try { return convert(); }
            catch (IOException error) when (options.exportMaterials)
            {
                options.exportMaterials=false;options.materials.Clear();
                var withoutMaterials=convert();
                warnings.Add("Materials/textures could not be decoded; exported model geometry, skeleton and animations without materials: "+error.Message);
                return withoutMaterials;
            }
        }
        if (obj is Mesh mesh)
        {
            var roots = FindMeshRoots(mesh);
            options.selectedMesh = mesh;
            if (roots.Count > 1) warnings.Add("Multiple model instances reference this mesh; exported the first deterministic related hierarchy, not overlapping variants.");
            model = Create(()=>roots.Count > 0 ? new ModelConverter(roots[0], options) : new ModelConverter(mesh, options));
            if (roots.Count == 0) warnings.Add("No related renderer/hierarchy was found. Exported mesh geometry and morph targets; no skeleton or animation was invented.");
            if (model.MeshList.Count == 0) throw new InvalidDataException("Selected mesh was not found in its referenced hierarchy");
            if(model.SkippedMeshInstances>0)warnings.Add($"This mesh has {model.SkippedMeshInstances+1} renderer instances in the related hierarchy. Mesh-asset export keeps one deterministic geometry instance; use GameObject export to retain all scene instances.");
        }
        else if (obj is AnimationClip clip)
        {
            var curves=AnimationCurves.Read(clip);
            var unsupported=curves.Tracks.Count(t=>t.Property is not ("position" or "rotation" or "scale" or "euler") && !t.Property.StartsWith("blendShape.",StringComparison.Ordinal));
            if(unsupported>0||curves.ObjectReferenceCurves>0)
                warnings.Add($"FBX does not preserve {unsupported} material/component curves and {curves.ObjectReferenceCurves} object-reference curves. Export ANIM and curves JSON to retain these source channels.");
            if(curves.Tracks.Count>0&&unsupported==curves.Tracks.Count)
                throw new InvalidDataException("This clip contains material/component animation only, not skeleton or morph tracks supported by FBX. Export ANIM and curves JSON instead.");
            var roots = AnimationPreview.FindRoots(obj.assetsFile.assetsManager, clip);
            if (roots.Count == 0) throw new InvalidDataException("No model references this AnimationClip in the imported files. Import its model DB as well, or export ANIM / curves JSON.");
            options.collectAnimations = false;
            var root = AnimationPreview.SelectBestRoot(roots,clip);
            if (roots.Count > 1) warnings.Add("This clip is shared by several model instances; exported the related hierarchy with the most bound animation tracks (then keys and geometry), with deterministic tie-breaking.");
            model = Create(()=>new ModelConverter(root, options, [clip]));
        }
        else model = Create(()=>obj switch { Animator a=>new ModelConverter(a,options), GameObject g=>new ModelConverter(g,options), _=>throw new NotSupportedException("FBX source type") });

        int unresolved = 0;
        foreach (var animation in model.AnimationList)
            unresolved += animation.TrackList.RemoveAll(t => string.IsNullOrEmpty(t.Path) || model.RootFrame.FindFrameByPath(t.Path) == null);
        if (unresolved > 0) warnings.Add($"{unresolved} animation tracks reference unavailable hierarchy nodes and were not exported.");
        model.AnimationList.RemoveAll(a=>!a.TrackList.Any(t=>t.Rotations.Count+t.Translations.Count+t.Scalings.Count+(t.BlendShape?.Keyframes.Count??0)>0));
        if (obj is AnimationClip && model.AnimationList.Count == 0) throw new InvalidDataException("No animation tracks could be bound to the related model.");
        if (settings.Animations && model.AnimationList.Count == 0) warnings.Add("No linked animation clips were available in the imported files; this FBX contains no animation takes.");
        if (model.MeshList.Count == 0) warnings.Add("This GameObject/Animator contains a hierarchy but no mesh geometry.");
        var previous = Directory.GetCurrentDirectory();
        try
        {
            ModelExporter.ExportFbx(path, model, new Fbx.ExportOptions { eulerFilter=true,filterPrecision=.25f,
                exportAllNodes=true,exportSkins=true,exportAnimations=settings.Animations || obj is AnimationClip,
                exportBlendShape=settings.BlendShapes,boneSize=10,scaleFactor=settings.Scale,fbxVersion=3,fbxFormat=0 });
        }
        finally { Directory.SetCurrentDirectory(previous); }
        using (var file = File.OpenRead(path))
        {
            Span<byte> header = stackalloc byte[23];
            if (file.Read(header) != header.Length || !header.SequenceEqual("Kaydara FBX Binary  \0\x1a\0"u8))
                throw new InvalidDataException("FBX exporter produced an invalid or incomplete file");
        }
        if(settings.Materials) foreach(var mat in options.materials)
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(path)!,Identity.SafeName(mat.Name)+"_"+mat.m_PathID+".material.json"),Exporters.ToJson(mat));
        File.WriteAllText(path+".export.json",System.Text.Json.JsonSerializer.Serialize(new {
            source=obj.assetsFile.fullName, pathId=obj.m_PathID.ToString(), type=obj.type.ToString(),
            meshes=model.MeshList.Count,vertices=model.MeshList.Sum(m=>m.VertexList.Count),
            triangles=model.MeshList.Sum(m=>m.SubmeshList.Sum(s=>s.FaceList.Count)),
            bones=model.MeshList.Sum(m=>m.BoneList?.Count??0),
            animations=(settings.Animations || obj is AnimationClip)?model.AnimationList.Select(a=>new {a.Name,tracks=a.TrackList.Count}).ToArray():[],
            textures=model.TextureList.Count,warnings
        },Program.Json));
        return warnings.ToArray();
    }
}
