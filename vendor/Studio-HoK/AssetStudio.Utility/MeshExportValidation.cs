using System;
using System.IO;
using System.Linq;

namespace AssetStudio
{
    public static class MeshExportValidation
    {
        public static void Validate(Mesh mesh)
        {
            int count = mesh.m_VertexCount;
            if (count <= 0) throw new InvalidDataException("Mesh has no vertices");
            void Channel(float[] data, string name, int min, int max, bool required = false)
            {
                if (data == null || data.Length == 0)
                {
                    if (required) throw new InvalidDataException("Missing mesh " + name);
                    return;
                }
                if (data.Length % count != 0 || data.Length / count < min || data.Length / count > max || !data.All(float.IsFinite))
                    throw new InvalidDataException("Invalid mesh " + name + " channel");
            }
            Channel(mesh.m_Vertices, "position", 3, 4, true);
            Channel(mesh.m_Normals, "normal", 3, 4);
            Channel(mesh.m_Colors, "color", 3, 4);
            Channel(mesh.m_Tangents, "tangent", 4, 4);
            for (int i = 0; i < 8; i++) Channel(mesh.GetUV(i), "UV" + i, 2, 4);
            if (mesh.m_Indices == null || mesh.m_Indices.Count % 3 != 0 || mesh.m_Indices.Any(i => i >= count))
                throw new InvalidDataException("Invalid mesh triangle indices");
            if (mesh.m_SubMeshes == null || mesh.m_SubMeshes.Sum(s => (long)s.indexCount) != mesh.m_Indices.Count)
                throw new InvalidDataException("Mesh submesh ranges do not cover all triangles");
            if (mesh.m_Skin?.Count > 0 && mesh.m_Skin.Count != count)
                throw new InvalidDataException("Mesh skin weights do not match vertices");
        }
    }
}
