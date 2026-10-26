using Lyra.Metadata.Exif;
using Lyra.Metadata.Iso;
using Lyra.Metadata.Support;

namespace Lyra.Metadata.Containers;

/// <summary>
/// A JPEG XL container: EXIF in an "Exif" box, XMP in an "xml " box, both at the top level - or
/// either one Brotli-compressed in a "brob" box, whose first four bytes name the box it holds. A
/// bare codestream (starting 0xFF0A) has no boxes and no metadata.
/// </summary>
public sealed class JxlContainer : IBufferedContainer
{
    private static ReadOnlySpan<byte> Signature => [0x00, 0x00, 0x00, 0x0C, 0x4A, 0x58, 0x4C, 0x20, 0x0D, 0x0A, 0x87, 0x0A];

    public static bool Recognizes(ReadOnlySpan<byte> head) => head.StartsWith(Signature);

    public static ContainerContents? Read(ReadOnlySpan<byte> data)
    {
        if (!Recognizes(data))
            return null;

        byte[]? exif = null;
        byte[]? xmp = null;

        foreach (var box in IsoBoxes.Children(data))
        {
            var type = box.Type;
            var body = box.Body;

            if (box.Is("brob"u8) && body.Length > 4)
            {
                type = body[..4];
                var wanted = (type.SequenceEqual("Exif"u8) && exif is null) || (type.SequenceEqual("xml "u8) && xmp is null);
                if (!wanted || Decompression.Brotli(body[4..], Limits.BlockBytes) is not { } unpacked)
                    continue;

                body = unpacked;
            }

            if (type.SequenceEqual("Exif"u8) && exif is null)
                exif = ExifPayload.LocateTiff(body);
            else if (type.SequenceEqual("xml "u8) && xmp is null && body.Length > 0)
                xmp = body.ToArray();
        }

        var container = new ContainerContents();
        container.AddIfPresent(MetadataBlockKind.Exif, exif);
        container.AddIfPresent(MetadataBlockKind.Xmp, xmp);
        return container;
    }
}