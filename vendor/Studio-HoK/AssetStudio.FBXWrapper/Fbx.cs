using AssetStudio.FbxInterop;
using AssetStudio.PInvoke;
using Newtonsoft.Json.Linq;
using System.IO;
using System;
using System.Linq;

namespace AssetStudio
{
    public static partial class Fbx
    {

        static Fbx()
        {
            DllLoader.PreloadDll(FbxDll.DllName);
        }

        public static Vector3 QuaternionToEuler(Quaternion q)
        {
            AsUtilQuaternionToEuler(q.X, q.Y, q.Z, q.W, out var x, out var y, out var z);
            return new Vector3(x, y, z);
        }

        public static Quaternion EulerToQuaternion(Vector3 v)
        {
            AsUtilEulerToQuaternion(v.X, v.Y, v.Z, out var x, out var y, out var z, out var w);
            return new Quaternion(x, y, z, w);
        }

        public static class Exporter
        {
            private static void SeparateJointGeometry(IImported imported)
            {
                // Some Unity rigs attach a renderer to a Transform which is also
                // a joint. Keep the joint identity and put geometry on an identity
                // child: readers must not have to treat one FBX node as both types.
                var bones = imported.MeshList?.SelectMany(m => m.BoneList ?? new()).Select(b => b.Path).ToHashSet();
                if (bones == null) return;
                foreach (var mesh in imported.MeshList.Where(m => bones.Contains(m.Path)))
                {
                    var frame = imported.RootFrame.FindFrameByPath(mesh.Path);
                    if (frame == null) continue;
                    var oldPath = mesh.Path;
                    var name = "Geometry"; int suffix = 0;
                    while (frame.FindChild(name, false) != null) name = "Geometry_" + ++suffix;
                    var child = new ImportedFrame { Name=name, LocalPosition=Vector3.Zero,
                        LocalRotation=new Quaternion(0,0,0,1), LocalScale=Vector3.One };
                    frame.AddChild(child);mesh.Path=child.Path;
                    foreach(var morph in imported.MorphList.Where(m=>m.Path==oldPath)) morph.Path=child.Path;
                    foreach(var animation in imported.AnimationList)
                    foreach(var track in animation.TrackList.Where(t=>t.Path==oldPath && t.BlendShape!=null).ToArray())
                    {
                        // A combined track must retain joint motion on the joint.
                        // Only the morph curve belongs to the geometry child.
                        if(track.Translations.Count+track.Rotations.Count+track.Scalings.Count>0)
                        {
                            animation.TrackList.Add(new ImportedAnimationKeyframedTrack { Path=child.Path, BlendShape=track.BlendShape });
                            track.BlendShape=null;
                        }
                        else track.Path=child.Path;
                    }
                }
            }

            public static void Export(string path, IImported imported, ExportOptions exportOptions)
            {
                SeparateJointGeometry(imported);
                var file = new FileInfo(path);
                var dir = file.Directory;

                if (!dir.Exists)
                {
                    dir.Create();
                }

                // FBX SDK can silently fail beyond MAX_PATH, even with a relative
                // filename. Keep the native work directory short; .NET handles
                // the user's final Unicode/long destination and sidecar textures.
                var staging = Path.Combine(Path.GetTempPath(), "hok-fbx-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(staging);
                var currentDir = Directory.GetCurrentDirectory();
                try
                {
                    Directory.SetCurrentDirectory(staging);
                    using (var exporter = new FbxExporter("model.fbx", imported, exportOptions))
                    {
                        exporter.Initialize();
                        exporter.ExportAll();
                    }
                    var generated = Path.Combine(staging, "model.fbx");
                    if (!File.Exists(generated) || new FileInfo(generated).Length < 64)
                        throw new IOException("FBX SDK did not generate a valid output file");
                    foreach (var extra in Directory.GetFiles(staging, "*", SearchOption.AllDirectories))
                    {
                        if (extra == generated) continue;
                        var target = Path.Combine(dir.FullName, Path.GetRelativePath(staging, extra));
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        File.Copy(extra, target, true);
                    }
                    File.Copy(generated, file.FullName, true);
                }
                finally
                {
                    Directory.SetCurrentDirectory(currentDir);
                    Directory.Delete(staging, true); // Only this call's GUID staging directory.
                }
            }
        }

        public record ExportOptions
        {
            public bool eulerFilter;
            public float filterPrecision;
            public bool exportAllNodes;
            public bool exportSkins;
            public bool exportAnimations;
            public bool exportBlendShape;
            public bool castToBone;
            public int boneSize;
            public float scaleFactor;
            public int fbxVersion;
            public int fbxFormat;
        }
    }
}
