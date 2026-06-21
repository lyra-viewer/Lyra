using System.Buffers.Binary;
using Lyra.Common;
using Lyra.Imaging.Content;
using Lyra.ManagedCodecs.Texture;
using Lyra.ManagedCodecs.Texture.Ktx;

namespace Lyra.Imaging.Decoding.Structure;

/// <summary>
/// Builds the file-structure inspector model for a Khronos Texture: the version-specific header
/// (KTX 1.x is OpenGL-enum based, KTX 2.0 is Vulkan-enum based with a level index), followed by one
/// entry per stored mip level. Pure presentation - it re-reads the documented header offsets rather
/// than threading every raw field through the decoder.
/// </summary>
internal static class KtxStructure
{
    public static IReadOnlyList<StructureGroup> Describe(byte[] file, TextureData texture)
    {
        var groups = new List<StructureGroup>
        {
            KtxShared.IsKtx2(file) ? Ktx2HeaderGroup(file) : Ktx1HeaderGroup(file),
        };

        foreach (var sr in texture.Subresources)
        {
            if (sr.ArrayLayer != 0 || sr.Face != 0)
                continue;

            groups.Add(new StructureGroup
            {
                Name = $"Mipmap Level {sr.MipLevel}",
                Description = $"{sr.Width}x{sr.Height} texture data",
                SizeBytes = sr.Data.Length,
                Fields =
                [
                    Pair("Width", $"{sr.Width} px"),
                    Pair("Height", $"{sr.Height} px"),
                    Pair("Data Size", Formatters.SizeToStr(sr.Data.Length)),
                ],
            });
        }

        return groups;
    }

    private static StructureGroup Ktx1HeaderGroup(byte[] f) => new()
    {
        Name = "KTX Header",
        Description = "KTX 1.1 file header (64 bytes)",
        SizeBytes = 64,
        Fields =
        [
            Pair("Identifier", "«KTX 11»"),
            Pair("Endianness", Hex(U32(f, 12))),
            Pair("glType", Hex(U32(f, 16))),
            Pair("glFormat", Hex(U32(f, 24))),
            Pair("glInternalFormat", Hex(U32(f, 28))),
            Pair("Width", $"{U32(f, 36)} px"),
            Pair("Height", $"{U32(f, 40)} px"),
            Pair("Depth", $"{U32(f, 44)}"),
            Pair("Array Elements", $"{U32(f, 48)}"),
            Pair("Faces", $"{U32(f, 52)}"),
            Pair("Mipmap Levels", $"{U32(f, 56)}"),
            Pair("Key/Value Bytes", $"{U32(f, 60)}"),
        ],
    };

    private static StructureGroup Ktx2HeaderGroup(byte[] f) => new()
    {
        Name = "KTX2 Header",
        Description = "KTX 2.0 file header (80 bytes incl. index)",
        SizeBytes = 80,
        Fields =
        [
            Pair("Identifier", "«KTX 20»"),
            Pair("vkFormat", $"{U32(f, 12)}"),
            Pair("Type Size", $"{U32(f, 16)}"),
            Pair("Width", $"{U32(f, 20)} px"),
            Pair("Height", $"{U32(f, 24)} px"),
            Pair("Depth", $"{U32(f, 28)}"),
            Pair("Layers", $"{U32(f, 32)}"),
            Pair("Faces", $"{U32(f, 36)}"),
            Pair("Levels", $"{U32(f, 40)}"),
            Pair("Supercompression", Supercompression(U32(f, 44))),
        ],
    };

    private static KeyValuePair<string, string> Pair(string key, string value) => new(key, value);

    private static uint U32(byte[] f, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(f.AsSpan(offset, 4));

    private static string Hex(uint value) => $"0x{value:X}";

    private static string Supercompression(uint scheme) => scheme switch
    {
        0 => "None",
        1 => "BasisLZ",
        2 => "Zstandard",
        3 => "ZLIB",
        _ => $"{scheme}",
    };
}