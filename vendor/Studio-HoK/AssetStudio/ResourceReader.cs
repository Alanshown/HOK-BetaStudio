using System.IO;

namespace AssetStudio
{
    public class ResourceReader
    {
        private bool needSearch;
        private string path;
        private SerializedFile assetsFile;
        private long offset;
        private long size;
        private BinaryReader reader;

        public int Size { get => (int)size; }

        public ResourceReader(string path, SerializedFile assetsFile, long offset, long size)
        {
            needSearch = true;
            this.path = path;
            this.assetsFile = assetsFile;
            this.offset = offset;
            this.size = size;
        }

        public ResourceReader(BinaryReader reader, long offset, long size)
        {
            this.reader = reader;
            this.offset = offset;
            this.size = size;
        }

        private BinaryReader GetReader()
        {
            if (needSearch)
            {
                var resourceFileName = Path.GetFileName(path.Replace('\\', '/'));
                if (assetsFile.assetsManager.TryGetResource(assetsFile, path, out reader) ||
                    assetsFile.assetsManager.TryGetResource(assetsFile, resourceFileName, out reader))
                {
                    needSearch = false;
                    return reader;
                }
                if (assetsFile.game.Type.IsHonorOfKings())
                {
                    var hashedPath = QtsVFSFile.Compute(path, true);
                    if (assetsFile.assetsManager.TryGetResource(assetsFile, hashedPath.ToString(), out reader))
                    {
                        needSearch = false;
                        return reader;
                    }
                }
                var assetsFileDirectory = Path.GetDirectoryName(assetsFile.originalPath ?? assetsFile.fullName);
                var resourceFilePath = AssetsManager.FindLocalFile(assetsFileDirectory, path);
                if (resourceFilePath != null)
                {
                    reader = assetsFile.assetsManager.OpenDiskResource(resourceFilePath);
                    needSearch = false;
                    return reader;
                }
                throw new FileNotFoundException($"Can't find the resource file {resourceFileName}");
            }
            else
            {
                return reader;
            }
        }

        public byte[] GetData()
        {
            var binaryReader = GetReader();
            binaryReader.BaseStream.Position = offset;
            return binaryReader.ReadBytes((int)size);
        }

        public void GetData(byte[] buff)
        {
            var binaryReader = GetReader();
            binaryReader.BaseStream.Position = offset;
            binaryReader.Read(buff, 0, (int)size);
        }

        public void WriteData(string path)
        {
            var binaryReader = GetReader();
            binaryReader.BaseStream.Position = offset;
            using (var writer = File.OpenWrite(path))
            {
                binaryReader.BaseStream.CopyTo(writer, size);
            }
        }
    }
}
