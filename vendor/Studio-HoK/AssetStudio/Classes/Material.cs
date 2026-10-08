using Newtonsoft.Json;
using System.Collections.Generic;

namespace AssetStudio
{
    public class UnityTexEnv
    {
        public PPtr<Texture> m_Texture;
        public Vector2 m_Scale;
        public Vector2 m_Offset;

        public UnityTexEnv(ObjectReader reader)
        {
            m_Texture = new PPtr<Texture>(reader);
            m_Scale = reader.ReadVector2();
            m_Offset = reader.ReadVector2();
            if (reader.Game.Type.IsArknightsEndfield())
            {
                var m_UVSetIndex = reader.ReadInt32();
            }
        }
    }

    public class UnityPropertySheet
    {
        [JsonConverter(typeof(KVPConverter<UnityTexEnv>))]
        public List<KeyValuePair<string, UnityTexEnv>> m_TexEnvs;
        [JsonConverter(typeof(KVPConverter<int>))]
        public List<KeyValuePair<string, int>> m_Ints;
        [JsonConverter(typeof(KVPConverter<float>))]
        public List<KeyValuePair<string, float>> m_Floats;
        [JsonConverter(typeof(KVPConverter<Color>))]
        public List<KeyValuePair<string, Color>> m_Colors;

        public UnityPropertySheet(ObjectReader reader)
        {
            var version = reader.version;

            int m_TexEnvsSize = reader.ReadInt32();
            m_TexEnvs = new List<KeyValuePair<string, UnityTexEnv>>();
            for (int i = 0; i < m_TexEnvsSize; i++)
            {
                m_TexEnvs.Add(new(reader.ReadAlignedString(), new UnityTexEnv(reader)));
            }

            if (version[0] >= 2021) //2021.1 and up
            {
                int m_IntsSize = reader.ReadInt32();
                m_Ints = new List<KeyValuePair<string, int>>();
                for (int i = 0; i < m_IntsSize; i++)
                {
                    m_Ints.Add(new(reader.ReadAlignedString(), reader.ReadInt32()));
                }
            }

            int m_FloatsSize = reader.ReadInt32();
            m_Floats = new List<KeyValuePair<string, float>>();
            for (int i = 0; i < m_FloatsSize; i++)
            {
                m_Floats.Add(new(reader.ReadAlignedString(), reader.ReadSingle()));
            }

            int m_ColorsSize = reader.ReadInt32();
            m_Colors = new List<KeyValuePair<string, Color>>();
            for (int i = 0; i < m_ColorsSize; i++)
            {
                m_Colors.Add(new(reader.ReadAlignedString(), reader.ReadColor4()));
            }
        }
    }

    public sealed class Material : NamedObject
    {
        public PPtr<Shader> m_Shader;
        public UnityPropertySheet m_SavedProperties;
        public List<KeyValuePair<string,string>> m_BuildTextureStacks;
        public object m_ShaderKeywords;
        public string[] m_ValidKeywords, m_InvalidKeywords, disabledShaderPasses;
        public uint m_LightmapFlags;
        public bool m_EnableInstancingVariants, m_DoubleSidedGI;
        public int m_CustomRenderQueue;
        public List<KeyValuePair<string,string>> stringTagMap = new List<KeyValuePair<string,string>>();

        public Material(ObjectReader reader) : base(reader)
        {
            m_Shader = new PPtr<Shader>(reader);

            if (version[0] == 4 && version[1] >= 1) //4.x
            {
                m_ShaderKeywords = reader.ReadStringArray();
            }

            if (version[0] > 2021 || (version[0] == 2021 && version[1] >= 3)) //2021.3 and up
            {
                m_ValidKeywords = reader.ReadStringArray();
                m_InvalidKeywords = reader.ReadStringArray();
            }
            else if (version[0] >= 5) //5.0 ~ 2021.2
            {
                m_ShaderKeywords = reader.ReadAlignedString();
            }

            if (version[0] >= 5) //5.0 and up
            {
                m_LightmapFlags = reader.ReadUInt32();
            }

            if (version[0] > 5 || (version[0] == 5 && version[1] >= 6)) //5.6 and up
            {
                m_EnableInstancingVariants = reader.ReadBoolean();
                if (version[0] >= 2017) m_DoubleSidedGI = reader.ReadBoolean();
                reader.AlignStream();
            }

            if (version[0] > 4 || (version[0] == 4 && version[1] >= 3)) //4.3 and up
            {
                m_CustomRenderQueue = reader.ReadInt32();
            }

            if (reader.Game.Type.IsLoveAndDeepspace())
            {
                var m_MaterialType = reader.ReadUInt32();
            }

            if (version[0] > 5 || (version[0] == 5 && version[1] >= 1)) //5.1 and up
            {
                var stringTagMapSize = reader.ReadInt32();
                for (int i = 0; i < stringTagMapSize; i++)
                {
                    var first = reader.ReadAlignedString();
                    var second = reader.ReadAlignedString();
                    stringTagMap.Add(new(first, second));
                }
            }

            if (reader.Game.Type.IsNaraka())
            {
                var value = reader.ReadInt32();
            }

            if (version[0] > 5 || (version[0] == 5 && version[1] >= 6)) //5.6 and up
            {
                disabledShaderPasses = reader.ReadStringArray();
            }

            m_SavedProperties = new UnityPropertySheet(reader);

            if (version[0] >= 2020)
            {
                int count=reader.ReadInt32();
                if(count<0||count>(reader.byteStart+reader.byteSize-reader.Position)/8)throw new System.IO.InvalidDataException("Invalid material texture-stack count");
                m_BuildTextureStacks=new List<KeyValuePair<string,string>>(count);
                for(int i=0;i<count;i++)m_BuildTextureStacks.Add(new(reader.ReadAlignedString(),reader.ReadAlignedString()));
            }
        }
    }
}
