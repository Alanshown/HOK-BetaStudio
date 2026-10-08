using System.IO;

namespace AssetStudio
{
    // Cubemap stores six complete face mip chains followed by source pointers.
    // Keep the entire stream: exporting it as one Texture2D silently lost 5 faces.
    public sealed class Cubemap : Texture2D
    {
        public PPtr<Texture2D>[] m_SourceTextures;

        public Cubemap(ObjectReader reader) : base(reader)
        {
            int count = reader.ReadInt32();
            if (count < 0 || count > (reader.byteStart + reader.byteSize - reader.Position) / 12)
                throw new InvalidDataException("Cubemap source references exceed the object boundary");
            m_SourceTextures = new PPtr<Texture2D>[count];
            for (int i = 0; i < count; i++) m_SourceTextures[i] = new PPtr<Texture2D>(reader);
        }

        public ResourceReader Face(int index)
        {
            if (index < 0 || index >= 6) throw new System.ArgumentOutOfRangeException(nameof(index));
            if (m_ImageCount != 6 || m_Width <= 0 || m_Width != m_Height || m_CompleteImageSize <= 0 ||
                image_data.Size != checked((long)m_CompleteImageSize * 6))
                throw new InvalidDataException("Cubemap stream is not six complete square face mip chains");
            return image_data.Slice((long)index * m_CompleteImageSize, m_CompleteImageSize);
        }
    }
}
