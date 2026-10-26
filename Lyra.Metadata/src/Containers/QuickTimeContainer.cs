using System.Text;
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
            else if (box.Type == "uuid" && IsXmp(stream, box) && stream.ReadBytesAt(box.BodyOffset + 16, box.BodySize - 16, Limits.BlockBytes) is { } packet && Packet(packet, box.CutShort) is { } xmp)
                container.Add(MetadataBlockKind.Xmp, xmp);

            container.Damaged |= box.CutShort;
            offset = box.End;
        }

        return container;
    }

    private static byte[]? Body(Stream stream, IsoBoxHeader box, int maxBytes) => stream.ReadBytesAt(box.BodyOffset, box.BodySize, maxBytes);

    /// <summary>A uuid box holding XMP, told by its first 16 bytes without reading the rest.</summary>
    private static bool IsXmp(Stream stream, IsoBoxHeader box)
    {
        Span<byte> uuid = stackalloc byte[16];
        return box.BodySize >= 16 && stream.ReadAt(box.BodyOffset, uuid) && uuid.SequenceEqual(IsoBoxes.XmpUuid);
    }

    private static void ReadMovie(ReadOnlySpan<byte> moov, ContainerContents container)
    {
        foreach (var box in IsoBoxes.Children(moov, keepCutShort: true))
        {
            if (box.Is("uuid"u8) && box.Body.StartsWith(CanonUuid))
                ReadCanon(box.Body[16..], container);
            else if (box.Is("udta"u8))
                ReadUserData(box.Body, container);
        }

        container.Damaged |= !IsoBoxes.IsWhole(moov);
    }

    /// <summary>The first directory of the TIFF in a CR3's CMT box; null for CMT3, the maker notes.</summary>
    public static TiffDirectoryKind? CanonTiffKind(string box) => box switch
    {
        "CMT1" => TiffDirectoryKind.Image,
        "CMT2" => TiffDirectoryKind.Exif,
        "CMT4" => TiffDirectoryKind.Gps,
        _ => null
    };

    private static void ReadCanon(ReadOnlySpan<byte> canon, ContainerContents container)
    {
        foreach (var box in IsoBoxes.Children(canon, keepCutShort: true))
            if (CanonTiffKind(Encoding.ASCII.GetString(box.Type)) is { } kind)
                container.AddExif(box.Body.ToArray(), kind);
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