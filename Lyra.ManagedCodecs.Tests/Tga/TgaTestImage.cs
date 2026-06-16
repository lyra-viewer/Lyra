namespace Lyra.ManagedCodecs.Tests.Tga;

/// <summary>
/// Minimal TGA byte assembler for tests. Builds the 18-byte header plus body so each test
/// can express exactly the bytes it wants and assert the decoded RGBA result.
/// </summary>
internal static class TgaTestImage
{
    // Image descriptor bits 4-5 select the origin; the low nibble holds the alpha bit count.
    public const byte TopLeft = 0x20;
    public const byte BottomLeft = 0x00;
    public const byte TopRight = 0x30;

    public static List<byte> Header(
        byte colorMapType,
        byte imageType,
        ushort cMapLength,
        byte cMapDepth,
        ushort width,
        ushort height,
        byte pixelDepth,
        byte descriptor)
    {
        List<byte> b =
        [
            0, // id length
            colorMapType,
            imageType,
            0, 0, // color map start
            (byte)(cMapLength & 0xFF), (byte)(cMapLength >> 8),
            cMapDepth,
            0, 0, // x offset
            0, 0, // y offset
            (byte)(width & 0xFF), (byte)(width >> 8),
            (byte)(height & 0xFF), (byte)(height >> 8),
            pixelDepth,
            descriptor,
        ];
        return b;
    }
}
