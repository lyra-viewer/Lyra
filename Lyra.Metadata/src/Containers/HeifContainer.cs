using System.Buffers.Binary;
using System.Text;
using Lyra.Metadata.Exif;
using Lyra.Metadata.Iso;
using Lyra.Metadata.Support;

namespace Lyra.Metadata.Containers;

public sealed record HeifFacts(int? BitDepthLuma, int? Rotation) : IFormatFacts;

public sealed class HeifContainer : IStreamedContainer
{
    /// <summary>The major brands read as HEIF; any other ftyp file it read as QuickTime.</summary>
    private static readonly string[] Brands = ["mif1", "msf1", "heic", "heix", "hevc", "hevx", "avif"];

    private const int MaxTopLevelBoxes = 1024;

    /// <summary>An hvcC is read as far as its NAL length size, in byte 21.</summary>
    private const int HvccLength = 22;

    public static bool Recognizes(ReadOnlySpan<byte> head) => IsoBoxes.MajorBrand(head) is { } brand && Brands.Contains(brand);

    private readonly record struct Property(string Type, byte[] Body);

    private readonly record struct Association(uint ItemId, ushort[] Properties);

    private readonly record struct Reference(string Type, uint From, uint[] To);

    private readonly record struct ItemInfo(uint Id, string Type);

    private readonly record struct Extent(ulong Offset, ulong Length);

    private readonly record struct ItemLocation(uint Id, int Construction, ulong BaseOffset, Extent[] Extents);

    private readonly record struct Item(ItemInfo Info, ItemLocation Location);

    public static ContainerContents? Read(Stream stream)
    {
        if (!stream.CanSeek)
            return null;

        using var _ = stream.KeepPosition();

        Span<byte> head = stackalloc byte[12];
        if (!stream.ReadAt(0, head) || !Recognizes(head))
            return null;

        var container = new ContainerContents();
        if (FindMeta(stream, container) is { } meta)
            ReadMeta(meta, stream, container);

        return container;
    }

    private static byte[]? FindMeta(Stream stream, ContainerContents container)
    {
        var offset = 0L;
        for (var i = 0; i < MaxTopLevelBoxes && offset < stream.Length; i++)
        {
            if (IsoBoxes.ReadHeader(stream, offset, stream.Length) is not { } box)
            {
                container.Damaged = true;
                return null;
            }

            if (box.Type == "meta")
            {
                var meta = stream.ReadBytesAt(box.BodyOffset, box.BodySize, Limits.BlockBytes);
                container.Damaged = meta is null;
                return meta;
            }

            offset = box.End;
        }

        return null;
    }

    private static void ReadMeta(ReadOnlySpan<byte> meta, Stream stream, ContainerContents container)
    {
        if (meta.Length < 4)
        {
            container.Damaged = true;
            return;
        }

        uint? primary = null;
        List<ItemInfo>? infos = null;
        List<Reference>? references = null;
        List<Property>? properties = null;
        List<Association>? associations = null;
        List<ItemLocation>? locations = null;
        var unreadable = false;

        foreach (var box in IsoBoxes.Children(meta[4..]))
        {
            if (box.Is("pitm"u8) && primary is null)
            {
                primary = ReadPrimary(box.Body);
                unreadable |= primary is null;
            }
            else if (box.Is("iinf"u8) && infos is null)
            {
                infos = ReadInfos(box.Body);
                unreadable |= infos is null;
            }
            else if (box.Is("iref"u8) && references is null)
            {
                references = ReadReferences(box.Body);
                unreadable |= references is null;
            }
            else if (box.Is("iprp"u8) && properties is null)
            {
                unreadable |= !ReadProperties(box.Body, out properties, out associations);
            }
            else if (box.Is("iloc"u8) && locations is null)
            {
                locations = ReadLocations(box.Body);
                unreadable |= locations is null;
            }
        }

        container.Damaged = unreadable || !IsoBoxes.IsWhole(meta[4..]);

        references ??= [];
        var described = references.Where(reference => reference.Type is "cdsc" or "mime" or "thmb").ToList();

        if (properties is not null && associations is not null)
            ReadImageProperties(primary ?? uint.MaxValue, references, described, properties, associations, container);

        if (infos is not null && locations is not null)
            ReadItems(described, infos, locations, stream, container);
    }

    private static void ReadImageProperties(uint primary, List<Reference> references, List<Reference> described, List<Property> properties, List<Association> associations, ContainerContents container)
    {
        var tiles = references.Where(reference => reference.From == primary && reference.Type == "dimg").SelectMany(reference => reference.To).ToHashSet();

        // The tiles of a grid carry what decoding needs, which the grid item itself may not.
        var tileProperties = associations
            .Where(association => tiles.Contains(association.ItemId))
            .SelectMany(association => association.Properties)
            .Where(index => Find(properties, index) is { Type: "hvcC" or "colr" })
            .Distinct();

        var main = Describe(Direct(primary, associations).Concat(tileProperties), properties, container);

        var luma = main.Luma;
        foreach (var reference in described)
        {
            var thumbnail = Describe(Direct(reference.From, associations), properties, container);
            luma ??= thumbnail.Luma;
        }

        if (luma is not null || main.Rotation is not null)
            container.AddFacts(new HeifFacts(luma, main.Rotation));
    }

    private readonly record struct Described(int? Luma, int? Rotation);

    private static Described Describe(IEnumerable<ushort> indexes, List<Property> properties, ContainerContents container)
    {
        int? luma = null;
        int? rotation = null;

        foreach (var index in indexes)
        {
            switch (Find(properties, index))
            {
                case { Type: "colr", Body: { Length: >= 4 } colr }:
                    var colorType = Encoding.ASCII.GetString(colr, 0, 4);
                    if (colorType != "nclx")
                        container.Add(MetadataBlockKind.Icc, colorType is "prof" or "rICC" ? colr[4..] : []);
                    break;

                case { Type: "hvcC", Body: { Length: >= HvccLength } hvcc }:
                    luma = (hvcc[17] & 7) + 8;
                    break;

                case { Type: "irot", Body: [var angle, ..] }:
                    rotation = (angle & 3) * 90;
                    break;
            }
        }

        return new Described(luma, rotation);
    }

    private static IEnumerable<ushort> Direct(uint item, List<Association> associations) =>
        associations.Where(association => association.ItemId == item).SelectMany(association => association.Properties);

    private static Property? Find(List<Property> properties, ushort index) => index >= 1 && index <= properties.Count ? properties[index - 1] : null;

    private static void ReadItems(List<Reference> described, List<ItemInfo> infos, List<ItemLocation> locations, Stream stream, ContainerContents container)
    {
        var items = new List<Item>();
        foreach (var reference in described)
        {
            var info = infos.FindIndex(each => each.Id == reference.From);
            var location = locations.FindIndex(each => each.Id == reference.From);
            if (info < 0 || location < 0)
            {
                container.Damaged = true;
                continue;
            }

            items.Add(new Item(infos[info], locations[location]));
        }

        foreach (var (info, location) in items.OrderBy(item => item.Location.BaseOffset).ThenBy(item => item.Location.Extents.FirstOrDefault().Offset))
        {
            if (location.Construction != 0 || location.Extents.Length != 1 || info.Type is not ("Exif" or "mime"))
                continue;

            var extent = location.Extents[0];
            if (ulong.MaxValue - location.BaseOffset < extent.Offset || ReadItem(stream, location.BaseOffset + extent.Offset, extent.Length) is not { } data)
                continue;

            if (info.Type == "mime")
                container.Add(MetadataBlockKind.Xmp, data);
            else if (ExifPayload.FromHeifItem(data) is { } tiff)
                container.Add(MetadataBlockKind.Exif, tiff);
        }
    }

    private static byte[]? ReadItem(Stream stream, ulong offset, ulong length) =>
        offset > long.MaxValue || length > long.MaxValue ? null : stream.ReadBytesAt((long)offset, (long)length, Limits.BlockBytes);
    
    private static uint? ReadPrimary(ReadOnlySpan<byte> pitm) => pitm switch
    {
        [0, _, _, _, ..] when pitm.Length >= 6 => BinaryPrimitives.ReadUInt16BigEndian(pitm[4..]),
        [> 0, _, _, _, ..] when pitm.Length >= 8 => BinaryPrimitives.ReadUInt32BigEndian(pitm[4..]),
        _ => null
    };

    private static List<ItemInfo>? ReadInfos(ReadOnlySpan<byte> iinf)
    {
        var start = iinf is [0, ..] ? 6 : 8;
        if (iinf.Length < start)
            return null;

        var infos = new List<ItemInfo>();
        foreach (var box in IsoBoxes.Children(iinf[start..]))
        {
            var infe = box.Body;
            if (!box.Is("infe"u8) || infe.Length < 4)
                continue;

            switch (infe[0])
            {
                case <= 1 when infe.Length >= 6:
                    infos.Add(new ItemInfo(BinaryPrimitives.ReadUInt16BigEndian(infe[4..]), string.Empty));
                    break;

                case 2 when infe.Length >= 12:
                    infos.Add(new ItemInfo(BinaryPrimitives.ReadUInt16BigEndian(infe[4..]), Encoding.ASCII.GetString(infe.Slice(8, 4))));
                    break;

                case > 2 when infe.Length >= 14:
                    infos.Add(new ItemInfo(BinaryPrimitives.ReadUInt32BigEndian(infe[4..]), Encoding.ASCII.GetString(infe.Slice(10, 4))));
                    break;
            }
        }

        return infos;
    }

    private static List<Reference>? ReadReferences(ReadOnlySpan<byte> iref)
    {
        if (iref.Length < 4)
            return null;

        var wide = iref[0] != 0;
        var idSize = wide ? 4 : 2;
        var references = new List<Reference>();

        foreach (var box in IsoBoxes.Children(iref[4..]))
        {
            var body = box.Body;
            if (body.Length < idSize + 2)
                continue;

            var count = BinaryPrimitives.ReadUInt16BigEndian(body[idSize..]);
            if (body.Length < idSize + 2 + count * idSize)
                continue;

            var to = new uint[count];
            for (var i = 0; i < count; i++)
                to[i] = Id(body[(idSize + 2 + i * idSize)..], wide);

            references.Add(new Reference(Encoding.ASCII.GetString(box.Type), Id(body, wide), to));
        }

        return references;
    }

    private static uint Id(ReadOnlySpan<byte> data, bool wide) =>
        wide ? BinaryPrimitives.ReadUInt32BigEndian(data) : BinaryPrimitives.ReadUInt16BigEndian(data);

    private static readonly string[] KeptProperties = ["colr", "hvcC", "irot"];

    private static bool ReadProperties(ReadOnlySpan<byte> iprp, out List<Property>? properties, out List<Association>? associations)
    {
        properties = null;
        associations = null;
        var readable = true;

        foreach (var box in IsoBoxes.Children(iprp))
        {
            if (box.Is("ipco"u8) && properties is null)
            {
                properties = [];
                foreach (var property in IsoBoxes.Children(box.Body))
                {
                    var name = Encoding.ASCII.GetString(property.Type);
                    properties.Add(new Property(name, KeptProperties.Contains(name) ? property.Body.ToArray() : []));
                }

                readable &= IsoBoxes.IsWhole(box.Body);
            }
            else if (box.Is("ipma"u8) && associations is null)
            {
                associations = ReadAssociations(box.Body);
                readable &= associations is not null;
            }
        }

        return readable;
    }

    private static List<Association>? ReadAssociations(ReadOnlySpan<byte> ipma)
    {
        if (ipma.Length < 8)
            return null;

        var wideIds = ipma[0] >= 1;
        var wideIndexes = (ipma[3] & 1) == 1;
        var count = BinaryPrimitives.ReadUInt32BigEndian(ipma[4..]);

        var associations = new List<Association>();
        var at = 8;
        for (var i = 0u; i < count; i++)
        {
            var idSize = wideIds ? 4 : 2;
            if (ipma.Length - at < idSize + 1)
                return null;

            var item = Id(ipma[at..], wideIds);
            int entries = ipma[at + idSize];
            at += idSize + 1;

            var indexSize = wideIndexes ? 2 : 1;
            if (ipma.Length - at < entries * indexSize)
                return null;

            var indexes = new ushort[entries];
            for (var e = 0; e < entries; e++, at += indexSize)
                indexes[e] = wideIndexes ? (ushort)(BinaryPrimitives.ReadUInt16BigEndian(ipma[at..]) & 0x7FFF) : (ushort)(ipma[at] & 0x7F);

            associations.Add(new Association(item, indexes));
        }

        return associations;
    }

    private static List<ItemLocation>? ReadLocations(ReadOnlySpan<byte> iloc)
    {
        if (iloc.Length < 6 || iloc[0] > 2)
            return null;

        var version = iloc[0];
        var offsetSize = iloc[4] >> 4;
        var lengthSize = iloc[4] & 0xF;
        var baseOffsetSize = iloc[5] >> 4;
        var indexSize = version is 1 or 2 ? iloc[5] & 0xF : 0;
        if (!Sized(offsetSize) || !Sized(lengthSize) || !Sized(baseOffsetSize) || !Sized(indexSize))
            return null;

        var reader = new ByteReader(iloc[6..], littleEndian: false);
        var count = version < 2 ? reader.Unsigned(2) : reader.Unsigned(4);

        var locations = new List<ItemLocation>();
        for (var i = 0UL; i < count && reader.Ok; i++)
        {
            var id = (uint)(version < 2 ? reader.Unsigned(2) : reader.Unsigned(4));
            var construction = version is 1 or 2 ? (int)(reader.Unsigned(2) & 0xF) : 0;
            reader.Skip(2); // data reference index
            var baseOffset = reader.Unsigned(baseOffsetSize);

            var extentCount = (int)reader.Unsigned(2);
            if (!reader.Ok || extentCount * (indexSize + offsetSize + lengthSize) > reader.Remaining)
                return null;

            var extents = new Extent[extentCount];
            for (var e = 0; e < extentCount; e++)
            {
                reader.Skip(indexSize);
                extents[e] = new Extent(reader.Unsigned(offsetSize), reader.Unsigned(lengthSize));
            }

            if (reader.Ok)
                locations.Add(new ItemLocation(id, construction, baseOffset, extents));
        }

        return reader.Ok ? locations : null;
    }

    private static bool Sized(int size) => size is 0 or 4 or 8;
}