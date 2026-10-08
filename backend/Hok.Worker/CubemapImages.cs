using AssetStudio;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.IO.Compression;
using System.Text.Json;

namespace Hok.Worker;
internal static class CubemapImages
{
    public static Image<Bgra32> Face(Cubemap cube, int index) =>
        cube.ConvertToImage(true, cube.Face(index)) ?? throw new InvalidDataException($"Cubemap face {index} cannot be decoded ({cube.m_TextureFormat})");

    // Storage order is preserved and explicitly labelled in the manifest. A
    // contact sheet is a preview, not a single-face replacement for a cubemap.
    public static Image<Bgra32> ContactSheet(Cubemap cube)
    {
        var sheet = new Image<Bgra32>(checked(cube.m_Width * 3), checked(cube.m_Height * 2));
        try
        {
            for (int i = 0; i < 6; i++)
            {
                using var face = Face(cube, i);
                sheet.Mutate(x => x.DrawImage(face, new Point(i % 3 * cube.m_Width, i / 3 * cube.m_Height), 1));
            }
            return sheet;
        }
        catch { sheet.Dispose(); throw; }
    }

    public static void WriteArchive(Cubemap cube, string path)
    {
        // Create the archive only after all six face ranges have been validated.
        for (int i = 0; i < 6; i++) _ = cube.Face(i);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        for (int i = 0; i < 6; i++)
        {
            using var image = Face(cube, i);
            using var entry = archive.CreateEntry($"face-{i}.png").Open();
            image.SaveAsPng(entry);
        }
        using (var entry = archive.CreateEntry("manifest.json").Open())
            JsonSerializer.Serialize(entry, new
            {
                name = cube.Name, type = "Cubemap", width = cube.m_Width, height = cube.m_Height,
                sourceFormat = cube.m_TextureFormat.ToString(), mipCount = cube.m_MipCount,
                exportedMip = 0, faces = Enumerable.Range(0, 6).Select(i => new { index = i, file = $"face-{i}.png" }),
                layout = "Storage order: 0..5. Contact sheet reads left-to-right, then top-to-bottom.",
                pixelConversion = "Display-ready 8-bit RGBA PNG, vertically flipped. HDR source precision and lower mip levels are retained in source-mips.bin; PNG is not a lossless representation of HDR values.",
                sourceBytesPerFace = cube.m_CompleteImageSize
            }, new JsonSerializerOptions { WriteIndented = true });
        using (var entry = archive.CreateEntry("source-mips.bin").Open()) entry.Write(cube.image_data.GetData());
    }
}
