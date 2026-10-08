using System.Collections.Generic;
using System.IO;

namespace AssetStudio
{
    public sealed class ShaderVariantCollection : NamedObject
    {
        public sealed class Variant
        {
            public string keywords;
            public int passType;
        }
        public sealed class ShaderVariants
        {
            public PPtr<Shader> shader;
            public List<Variant> variants = new List<Variant>();
        }
        public List<ShaderVariants> m_Shaders = new List<ShaderVariants>();

        public ShaderVariantCollection(ObjectReader reader) : base(reader)
        {
            int count = Count(reader, 16);
            for (int i = 0; i < count; i++)
            {
                var shader = new ShaderVariants { shader = new PPtr<Shader>(reader) };
                int variants = Count(reader, 8);
                for (int j = 0; j < variants; j++)
                    shader.variants.Add(new Variant { keywords = reader.ReadAlignedString(), passType = reader.ReadInt32() });
                m_Shaders.Add(shader);
            }
            // HOK appends a platform pipeline-state cache. Keep the reader at
            // this boundary so diagnostics do not call that unknown data parsed.
        }

        static int Count(ObjectReader reader, int minimumItemBytes)
        {
            int value = reader.ReadInt32();
            if (value < 0 || value > (reader.byteStart + reader.byteSize - reader.Position) / minimumItemBytes)
                throw new InvalidDataException("Shader variant table exceeds the object boundary");
            return value;
        }
    }
}
