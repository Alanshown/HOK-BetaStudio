using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;

namespace AssetStudio
{
    public sealed class PPtr<T> : IYAMLExportable, IObjectReference where T : Object
    {
        public int m_FileID;
        public long m_PathID;

        private SerializedFile assetsFile;
        [Newtonsoft.Json.JsonIgnore] public int FileId => m_FileID;
        [Newtonsoft.Json.JsonIgnore] public long PathId => m_PathID;
        [Newtonsoft.Json.JsonIgnore] public long? SerializedByteOffset { get; private set; }
        [Newtonsoft.Json.JsonIgnore] public long? ObjectByteOffset { get; private set; }
        [Newtonsoft.Json.JsonIgnore] public string ExpectedType => typeof(T).Name;
        public ReferenceResolution ResolveEvidence() => assetsFile.assetsManager.ResolveReference(assetsFile,m_FileID,m_PathID);
        
        public string Name => TryGet(out var obj) ? obj.Name : string.Empty;

        public PPtr(int m_FileID,  long m_PathID, SerializedFile assetsFile)
        {
            this.m_FileID = m_FileID;
            this.m_PathID = m_PathID;
            this.assetsFile = assetsFile;
        }

        public PPtr(ObjectReader reader)
        {
            SerializedByteOffset=reader.Position;
            ObjectByteOffset=reader.Position-reader.byteStart;
            m_FileID = reader.ReadInt32();
            m_PathID = reader.m_Version < SerializedFileFormatVersion.Unknown_14 ? reader.ReadInt32() : reader.ReadInt64();
            assetsFile = reader.assetsFile;
        }

        public YAMLNode ExportYAML(int[] version)
        {
            var node = new YAMLMappingNode();
            node.Style = MappingStyle.Flow;
            node.Add("fileID", m_FileID);
            return node;
        }

        private bool TryGetAssetsFile(out SerializedFile result)
        {
            var resolution=ResolveEvidence();
            result=resolution.TargetFile;
            return resolution.CanRead;
        }

        public bool TryGet(out T result)
        {
            if (TryGetAssetsFile(out var sourceFile))
            {
                if (sourceFile.ObjectsDic.TryGetValue(m_PathID, out var obj))
                {
                    obj = DeferredObject.Resolve(obj);
                    if (obj is T variable)
                    {
                        result = variable;
                        return true;
                    }
                }
            }

            result = null;
            return false;
        }

        public bool TryGet<T2>(out T2 result) where T2 : Object
        {
            if (TryGetAssetsFile(out var sourceFile))
            {
                if (sourceFile.ObjectsDic.TryGetValue(m_PathID, out var obj))
                {
                    obj = DeferredObject.Resolve(obj);
                    if (obj is T2 variable)
                    {
                        result = variable;
                        return true;
                    }
                }
            }

            result = null;
            return false;
        }

        public void Set(T m_Object)
        {
            SerializedByteOffset=ObjectByteOffset=null; // Programmatic pointer has no original field bytes.
            var name = m_Object.assetsFile.fileName;
            if (ReferenceEquals(assetsFile, m_Object.assetsFile))
            {
                m_FileID = 0;
            }
            else
            {
                m_FileID = assetsFile.m_Externals.FindIndex(x => ReferenceEquals(assetsFile.assetsManager.ResolveExternal(assetsFile, x), m_Object.assetsFile));
                if (m_FileID == -1)
                {
                    assetsFile.m_Externals.Add(new FileIdentifier
                    {
                        fileName = name,
                        pathName = m_Object.assetsFile.fullName
                    });
                    m_FileID = assetsFile.m_Externals.Count;
                }
                else
                {
                    m_FileID += 1;
                }
            }

            m_PathID = m_Object.m_PathID;
        }

        public PPtr<T2> Cast<T2>() where T2 : Object
        {
            return new PPtr<T2>(m_FileID, m_PathID, assetsFile) { SerializedByteOffset=SerializedByteOffset, ObjectByteOffset=ObjectByteOffset };
        }

        public bool IsNull => m_PathID == 0 || m_FileID < 0;
    }
}
