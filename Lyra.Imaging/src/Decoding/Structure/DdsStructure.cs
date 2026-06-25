using System.Buffers.Binary;
using System.Text;
using Lyra.Common;
using Lyra.Imaging.Content;
using Lyra.ManagedCodecs.Texture;

namespace Lyra.Imaging.Decoding.Structure;

/// <summary>
/// Builds the file-structure inspector model for a DDS: the header, pixel-format and capabilities
/// sub-structures (read straight from the raw bytes), followed by one entry per stored mip level.
/// Pure presentation - it re-reads the documented header offsets rather than threading every raw
/// field through the decoder.
/// </summary>
internal static class DdsStructure
{
    public static IReadOnlyList<StructureGroup> Describe(byte[] file, TextureData texture)
    {
        var groups = new List<StructureGroup>
        {
            HeaderGroup(file),
            PixelFormatGroup(file),
            CapabilitiesGroup(file),
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

    private static StructureGroup HeaderGroup(byte[] f) => new()
    {
        Name = "DDS Header",
        Description = "Main DDS file header",
        SizeBytes = 128,
        Fields =
        [
            Pair("Magic", $"\"{Ascii(f, 0, 4)}\""),
            Pair("Header Size", $"{U32(f, 4)} bytes"),
            Pair("Flags", Hex(U32(f, 8))),
            Pair("Width", $"{U32(f, 16)} px"),
            Pair("Height", $"{U32(f, 12)} px"),
            Pair("Pitch/LinearSize", $"{U32(f, 20)}"),
            Pair("Depth", $"{U32(f, 24)}"),
            Pair("Mipmap Count", $"{U32(f, 28)}"),
        ],
    };

    private static StructureGroup PixelFormatGroup(byte[] f) => new()
    {
        Name = "Pixel Format",
        Description = "DDS pixel format structure",
        SizeBytes = 32,
        Fields =
        [
            Pair("Size", $"{U32(f, 76)} bytes"),
            Pair("Flags", Hex(U32(f, 80))),
            Pair("FourCC", FourCc(U32(f, 84))),
            Pair("RGB Bit Count", $"{U32(f, 88)}"),
            Pair("R Mask", Hex(U32(f, 92))),
            Pair("G Mask", Hex(U32(f, 96))),
            Pair("B Mask", Hex(U32(f, 100))),
            Pair("A Mask", Hex(U32(f, 104))),
        ],
    };

    private static StructureGroup CapabilitiesGroup(byte[] f)
    {
        var caps = U32(f, 108);
        var caps2 = U32(f, 112);

        return new StructureGroup
        {
            Name = "Capabilities",
            Description = "Surface capabilities flags",
            SizeBytes = 16,
            Fields =
            [
                Pair("Caps", Hex(caps)),
                Pair("Caps2", Hex(caps2)),
                Pair("Complex", YesNo((caps & 0x8) != 0)),
                Pair("Mipmap", YesNo((caps & 0x400000) != 0)),
                Pair("Cubemap", YesNo((caps2 & 0x200) != 0)),
                Pair("Volume", YesNo((caps2 & 0x200000) != 0)),
            ],
        };
    }

    // --------------------------------------------------------
    //  Formatting helpers
    // --------------------------------------------------------

    private static KeyValuePair<string, string> Pair(string key, string value) => new(key, value);

    private static uint U32(byte[] f, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(f.AsSpan(offset, 4));

    private static string Ascii(byte[] f, int offset, int length) => Encoding.ASCII.GetString(f, offset, length);

    private static string Hex(uint value) => $"0x{value:X}";

    private static string YesNo(bool value) => value ? "Yes" : "No";

    private static string FourCc(uint value)
    {
        if (value == 0)
            return "0";

        // A real four-char code has data in its high bytes; a numeric D3DFORMAT shows as its char.
        Span<char> chars =
        [
            (char)(value & 0xFF), (char)((value >> 8) & 0xFF),
            (char)((value >> 16) & 0xFF), (char)((value >> 24) & 0xFF),
        ];
        return new string(chars).TrimEnd('\0');
    }
}
