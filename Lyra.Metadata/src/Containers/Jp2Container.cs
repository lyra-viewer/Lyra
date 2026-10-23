using System.Buffers.Binary;
using Lyra.Metadata.Exif;
using Lyra.Metadata.Iso;

namespace Lyra.Metadata.Containers;

public readonly record struct Jp2Size(long Width, long Height, int Components);

/// <summary>
/// A JPEG 2000 file: EXIF and XMP in top-level "uuid" boxes, told apart by their UUID, and an ICC
/// profile in the header's "colr" box. A bare codestream (starting 0xFF4F) has no boxes and no metadata.
/// </summary>
public sealed class Jp2Container : IBufferedContainer
{
    private static ReadOnlySpan<byte> Signature => [0x00, 0x00, 0x00, 0x0C, 0x6A, 0x50, 0x20, 0x20, 0x0D, 0x0A, 0x87, 0x0A];

    // "JpgTiffExif->JP2", the UUID JP2 writers agreed on for an EXIF block.
    private static ReadOnlySpan<byte> ExifUuid => "JpgTiffExif->JP2"u8;

    public static bool Recognizes(ReadOnlySpan<byte> head) => head.StartsWith(Signature);

    public static ContainerContents? Read(ReadOnlySpan<byte> data)
    {
        if (!Recognizes(data))
            return null;

        byte[]? exif = null;
        byte[]? xmp = null;
        byte[]? icc = null;

        foreach (var box in IsoBoxes.Children(data))
        {
            if (box.Is("jp2h"u8) && icc is null)
                icc = ReadIcc(box.Body);

            if (!box.Is("uuid"u8) || box.Body.Length <= 16)
                continue;

            var uuid = box.Body[..16];
            var content = box.Body[16..];

            if (uuid.SequenceEqual(ExifUuid) && exif is null)
                exif = ExifPayload.LocateTiff(content);
            else if (uuid.SequenceEqual(IsoBoxes.XmpUuid) && xmp is null)
                xmp = content.ToArray();
        }

        var container = new ContainerContents();
        container.AddIfPresent(MetadataBlockKind.Exif, exif);
        container.AddIfPresent(MetadataBlockKind.Xmp, xmp);
        container.AddIfPresent(MetadataBlockKind.Icc, icc);
        return container;
    }

    /// <summary>
    /// The profile of the first "colr" box that carries one: method 2, a restricted ICC profile, or
    /// JPX's method 3, any ICC profile - after the method, precedence and approximation bytes.
    /// </summary>
    private static byte[]? ReadIcc(ReadOnlySpan<byte> header)
    {
        foreach (var box in IsoBoxes.Children(header))
            if (box.Is("colr"u8) && box.Body is [2 or 3, _, _, _, ..])
                return box.Body[3..].ToArray();

        return null;
    }

    private const ushort StartOfCodestream = 0xFF4F;
    private const ushort ImageAndTileSize = 0xFF51;

    /// <summary>
    /// The size a JP2's image header box declares, or a bare codestream's SIZ marker; null when
    /// neither can be read.
    /// </summary>
    public static Jp2Size? ReadSize(ReadOnlySpan<byte> data) => Recognizes(data) ? ReadJp2Size(data) : ReadCodestreamSize(data);

    /// <summary>The image header box inside the JP2 header box, or else the codestream box.</summary>
    private static Jp2Size? ReadJp2Size(ReadOnlySpan<byte> data)
    {
        foreach (var box in IsoBoxes.Children(data))
        {
            if (box.Is("jp2h"u8) && ReadImageHeader(box.Body) is { } size)
                return size;

            if (box.Is("jp2c"u8))
                return ReadCodestreamSize(box.Body);
        }

        return null;
    }

    private static Jp2Size? ReadImageHeader(ReadOnlySpan<byte> header)
    {
        foreach (var (type, payload) in IsoBoxes.Children(header))
        {
            if (type.SequenceEqual("ihdr"u8) && payload.Length >= 10)
                return new Jp2Size(
                    Width: BinaryPrimitives.ReadUInt32BigEndian(payload[4..]),
                    Height: BinaryPrimitives.ReadUInt32BigEndian(payload),
                    Components: BinaryPrimitives.ReadUInt16BigEndian(payload[8..])
                );
        }

        return null;
    }

    /// <summary>The SIZ marker, which a codestream must carry straight after its start marker.</summary>
    private static Jp2Size? ReadCodestreamSize(ReadOnlySpan<byte> codestream)
    {
        if (codestream.Length < 42
            || BinaryPrimitives.ReadUInt16BigEndian(codestream) != StartOfCodestream
            || BinaryPrimitives.ReadUInt16BigEndian(codestream[2..]) != ImageAndTileSize)
            return null;

        var siz = codestream[6..];
        long xEnd = BinaryPrimitives.ReadUInt32BigEndian(siz[2..]);
        long yEnd = BinaryPrimitives.ReadUInt32BigEndian(siz[6..]);
        long xOrigin = BinaryPrimitives.ReadUInt32BigEndian(siz[10..]);
        long yOrigin = BinaryPrimitives.ReadUInt32BigEndian(siz[14..]);
        int components = BinaryPrimitives.ReadUInt16BigEndian(siz[34..]);

        return xEnd <= xOrigin || yEnd <= yOrigin ? null : new Jp2Size(xEnd - xOrigin, yEnd - yOrigin, components);
    }
}