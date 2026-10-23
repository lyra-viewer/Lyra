using Lyra.Metadata.Iso;
using Lyra.Metadata.Support;
using Lyra.Metadata.Tiff;

namespace Lyra.Metadata.Containers;

public sealed class QuickTimeContainer : IStreamedContainer
{
    private static ReadOnlySpan<byte> CanonUuid => [0x85, 0xC0, 0xB6, 0x87, 0x82, 0x0F, 0x11, 0xE0, 0x81, 0x11, 0xF4, 0xCE, 0x46, 0x2B, 0x6A, 0x48];

    private const int MaxTopLevelBoxes = 1024;

    public static bool Recognizes(ReadOnlySpan<byte> head) => IsoBoxes.MajorBrand(head) is not null && !HeifContainer.Recognizes(head);

    public static ContainerContents? Read(Stream stream)
    {
        if (!stream.CanSeek)
            return null;

        using var _ = stream.KeepPosition();

        Span<byte> head = stackalloc byte[12];
        if (!stream.ReadAt(0, head) || !Recognizes(head))
            return null;

        var container = new ContainerContents();
        var offset = 0L;
        for (var i = 0; i < MaxTopLevelBoxes && offset < stream.Length; i++)
        {
            if (IsoBoxes.ReadHeader(stream, offset, stream.Length, keepCutShort: true) is not { } box)
            {
                container.Damaged = true;
                break;
            }

            if (box.Type == "moov" && Body(stream, box, Limits.MovieBytes) is { } moov)
                ReadMovie(moov, container);
            else if (box.Type == "uuid" && box.BodySize >= 16 && Body(stream, box, Limits.BlockBytes + 16) is { } uuid && uuid.AsSpan(0, 16).SequenceEqual(IsoBoxes.XmpUuid) && Packet(uuid.AsSpan(16), box.CutShort) is { } xmp)
                container.Add(MetadataBlockKind.Xmp, xmp);

            container.Damaged |= box.CutShort;
            offset = box.End;
        }

        return container;
    }

    private static byte[]? Body(Stream stream, IsoBoxHeader box, int maxBytes) => stream.ReadBytesAt(box.BodyOffset, box.BodySize, maxBytes);

    private static void ReadMovie(ReadOnlySpan<byte> moov, ContainerContents container)
    {
        foreach (var (type, body) in IsoBoxes.Children(moov, keepCutShort: true))
        {
            if (type.SequenceEqual("uuid"u8) && body.StartsWith(CanonUuid))
                ReadCanon(body[16..], container);
            else if (type.SequenceEqual("udta"u8))
                ReadUserData(body, container);
        }

        container.Damaged |= !IsoBoxes.IsWhole(moov);
    }

    private static void ReadCanon(ReadOnlySpan<byte> canon, ContainerContents container)
    {
        foreach (var box in IsoBoxes.Children(canon, keepCutShort: true))
        {
            if (box.Is("CMT1"u8))
                container.AddExif(box.Body.ToArray(), TiffDirectoryKind.Image);
            else if (box.Is("CMT2"u8))
                container.AddExif(box.Body.ToArray(), TiffDirectoryKind.Exif);
            else if (box.Is("CMT4"u8))
                container.AddExif(box.Body.ToArray(), TiffDirectoryKind.Gps);
        }
    }

    private static void ReadUserData(ReadOnlySpan<byte> udta, ContainerContents container)
    {
        foreach (var box in IsoBoxes.Children(udta, keepCutShort: true))
            if (box.Is("XMP_"u8) && Packet(box.Body, box.CutShort) is { } xmp)
                container.Add(MetadataBlockKind.Xmp, xmp);
    }

    private static byte[]? Packet(ReadOnlySpan<byte> data, bool cutShort)
    {
        var nul = data.IndexOf((byte)0);
        if (nul < 0)
            return cutShort ? null : data.ToArray();

        return data[..nul].ToArray();
    }
}