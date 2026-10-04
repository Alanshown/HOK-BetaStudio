using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;

namespace AssetStudio
{
    public sealed class PPtr<T> : IYAMLExportable where T : Object
    {
        public int m_FileID;
        public long m_PathID;

        private SerializedFile assetsFile;
        
        public string Name => TryGet(out var obj) ? obj.Name : string.Empty;

        public PPtr(int m_FileID,  long m_PathID, SerializedFile assetsFile)
        {
            this.m_FileID = m_FileID;
            this.m_PathID = m_PathID;
            this.assetsFile = assetsFile;
        }

        public PPtr(ObjectReader reader)
        {
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
            result = null;
            if (m_FileID == 0)
            {
                result = assetsFile;
                return true;
            }

            if (m_FileID > 0 && m_FileID - 1 < assetsFile.m_Externals.Count)
            {
                var external = assetsFile.m_Externals[m_FileID - 1];
                // HOK's repacked QTS SerializedFiles retain GUID-only external
                // slots while referenced objects are embedded in the same DB.
                // Prefer local objects, then a unique same-source target;
                // never borrow a PathID from a different DB.
                if (assetsFile.game.Type.IsHonorOfKings()
                    && string.IsNullOrEmpty(external.pathName)
                    && string.IsNullOrEmpty(external.fileName))
                {
                    result = assetsFile.assetsManager.ResolveHokObjectReference(assetsFile, m_PathID);
                    return result != null;
                }
                // Resolve against the owner scope, not a basename or a cached list index.
                result = assetsFile.assetsManager.ResolveExternal(assetsFile, external);
                return result != null;
            }

            return false;
        }

        public bool TryGet(out T result)
        {
            if (TryGetAssetsFile(out var sourceFile))
            {
                if (sourceFile.ObjectsDic.TryGetValue(m_PathID, out var obj))
                {
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
            return new PPtr<T2>(m_FileID, m_PathID, assetsFile);
        }

        public bool IsNull => m_PathID == 0 || m_FileID < 0;
    }
}
