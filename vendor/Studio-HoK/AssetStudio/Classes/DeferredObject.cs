using System;
using System.IO;

namespace AssetStudio
{
    // The object-table identity/name is always indexed. Heavy geometry/curves
    // are decoded on demand in large workspaces, not held for every mesh at once.
    public sealed class DeferredObject : NamedObject
    {
        WeakReference<Object> value;
        string failure;
        public DeferredObject(ObjectReader reader) : base(reader) { }
        public void ReleaseDecodedValue() => value = null;
        public static bool CanDefer(ClassIDType type) => type is ClassIDType.Mesh or ClassIDType.AnimationClip or ClassIDType.Shader or ClassIDType.Material or ClassIDType.Font or ClassIDType.AudioClip or ClassIDType.VideoClip or ClassIDType.MovieTexture;
        public Object Resolve()
        {
            if (value != null && value.TryGetTarget(out var cached)) return cached;
            if (failure != null) throw new InvalidDataException(failure);
            var status = assetsFile.ParseStatuses[m_PathID];
            try
            {
                Object parsed = type switch
                {
                    ClassIDType.Mesh => new Mesh(reader),
                    ClassIDType.AnimationClip => new AnimationClip(reader),
                    ClassIDType.Shader => new Shader(reader),
                    ClassIDType.Material => new Material(reader),
                    ClassIDType.Font => new Font(reader),
                    ClassIDType.AudioClip => new AudioClip(reader),
                    ClassIDType.VideoClip => new VideoClip(reader),
                    ClassIDType.MovieTexture => new MovieTexture(reader),
                    _ => throw new NotSupportedException(type.ToString())
                };
                status.Parser = parsed.GetType().Name; status.Typed = true;
                status.ConsumedBytes = reader.Position - reader.byteStart;
                status.RemainingBytes = reader.byteSize - status.ConsumedBytes;
                status.Status = status.RemainingBytes == 0 ? "typed-complete" : "typed-partial";
                value = new WeakReference<Object>(parsed);
                return parsed;
            }
            catch (Exception e)
            {
                failure = e.GetBaseException().Message;
                status.Error = failure; status.Status = "parser-failed"; status.Typed = false;
                status.ConsumedBytes = reader.Position - reader.byteStart;
                status.RemainingBytes = reader.byteSize - status.ConsumedBytes;
                throw;
            }
        }
        public static Object Resolve(Object obj) => obj is DeferredObject d ? d.Resolve() : obj;
    }
}
