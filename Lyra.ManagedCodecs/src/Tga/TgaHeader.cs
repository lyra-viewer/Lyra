using System.Buffers.Binary;

namespace Lyra.ManagedCodecs.Tga;

/// <summary>
/// The fixed 18-byte TGA file header. All multibyte fields are little-endian.
/// <see href="https://www.fileformat.info/format/tga/egff.htm"/>
/// </summary>
internal readonly struct TgaHeader
{
    public const int Size = 18;

    public byte IdLength { get; init; }
    public byte ColorMapType { get; init; }
    public TgaImageType ImageType { get; init; }

    public ushort CMapStart { get; init; }
    public ushort CMapLength { get; init; }
    public byte CMapDepth { get; init; }

    public ushort XOffset { get; init; }
    public ushort YOffset { get; init; }
    public ushort Width { get; init; }
    public ushort Height { get; init; }
    public byte PixelDepth { get; init; }
    public byte ImageDescriptor { get; init; }

    /// <summary>Number of attribute (alpha) bits, from the low nibble of the image descriptor.</summary>
    public int AttributeBits => ImageDescriptor & 0x0F;

    /// <summary>Image origin, from bits 4-5 of the image descriptor.</summary>
    public TgaImageOrigin Origin => (TgaImageOrigin)((ImageDescriptor & 0x30) >> 4);

    public static TgaHeader Parse(ReadOnlySpan<byte> data) => new()
    {
        IdLength = data[0],
        ColorMapType = data[1],
        ImageType = (TgaImageType)data[2],
        CMapStart = BinaryPrimitives.ReadUInt16LittleEndian(data[3..]),
        CMapLength = BinaryPrimitives.ReadUInt16LittleEndian(data[5..]),
        CMapDepth = data[7],
        XOffset = BinaryPrimitives.ReadUInt16LittleEndian(data[8..]),
        YOffset = BinaryPrimitives.ReadUInt16LittleEndian(data[10..]),
        Width = BinaryPrimitives.ReadUInt16LittleEndian(data[12..]),
        Height = BinaryPrimitives.ReadUInt16LittleEndian(data[14..]),
        PixelDepth = data[16],
        ImageDescriptor = data[17],
    };
}

/// <summary>
/// The corner of the display the first stored pixel maps to (image descriptor bits 4-5).
/// </summary>
internal enum TgaImageOrigin
{
    BottomLeft = 0,
    BottomRight = 1,
    TopLeft = 2,
    TopRight = 3,
}