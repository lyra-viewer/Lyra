using System.Buffers.Binary;
using System.Text;

namespace Lyra.Metadata.Tests.Support;

/// <summary>Builds ISO media files from boxes: HEIF stills with their items and properties, and CR3-like movies.</summary>
internal static class IsoFixture
{
    public static byte[] Box(string type, params byte[][] parts)
    {
        byte[] body = [.. parts.SelectMany(part => part)];
        return [.. U32((uint)(8 + body.Length)), .. Ascii(type), .. body];
    }

    public static byte[] FullBox(string type, byte version, params byte[][] parts) => FullBox(type, version, 0, parts);

    public static byte[] FullBox(string type, byte version, uint flags, params byte[][] parts)
    {
        return Box(type, [[version, (byte)(flags >> 16), (byte)(flags >> 8), (byte)flags], .. parts]);
    }

    public static byte[] Ftyp(string brand) => Box("ftyp", Ascii(brand), U32(0), Ascii(brand), Ascii("mif1"));

    public static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);

    public static byte[] U16(int value)
    {
        var bytes = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, (ushort)value);
        return bytes;
    }

    public static byte[] U32(uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        return bytes;
    }

    // HEIF properties

    public static byte[] Colr(string colorType, byte[] profile) => Box("colr", Ascii(colorType), profile);

    public static byte[] Nclx() => Box("colr", Ascii("nclx"), U16(1), U16(13), U16(1), [0x80]);

    public static byte[] Hvcc(int lumaBits)
    {
        var body = new byte[23];
        body[0] = 1;
        body[17] = (byte)(0xF8 | (lumaBits - 8));
        body[18] = (byte)(0xF8 | (lumaBits - 8));
        return Box("hvcC", body);
    }

    public static byte[] Irot(int angle) => Box("irot", [(byte)angle]);

    public static byte[] Ispe(uint width, uint height) => FullBox("ispe", 0, U32(width), U32(height));

    // HEIF items

    /// <param name="Construction">0 places the data in mdat; anything else is written but points nowhere.</param>
    internal sealed record Item(uint Id, string Type, byte[] Data, int Construction = 0, string? ContentType = null);

    internal sealed record Reference(string Type, uint From, params uint[] To);

    internal sealed record Association(uint Item, params int[] Properties);

    internal sealed class Heif
    {
        public string Brand { get; init; } = "heic";
        public uint? Primary { get; init; } = 1;
        public List<Item> Items { get; init; } = [];
        public List<Reference> References { get; init; } = [];
        public List<byte[]> Properties { get; init; } = [];
        public List<Association> Associations { get; init; } = [];

        /// <summary>The order the items' data is laid out in mdat, by item id; item order when null.</summary>
        public uint[]? DataOrder { get; init; }

        public byte[] Build()
        {
            var ftyp = Ftyp(Brand);
            var order = DataOrder?.Select(id => Items.Single(item => item.Id == id)).ToList() ?? Items;

            var placeholder = Meta(new Dictionary<uint, uint>());
            var dataStart = (uint)(ftyp.Length + placeholder.Length + 8);

            var offsets = new Dictionary<uint, uint>();
            var at = dataStart;
            foreach (var item in order)
            {
                offsets[item.Id] = at;
                at += (uint)item.Data.Length;
            }

            return [.. ftyp, .. Meta(offsets), .. Box("mdat", [.. order.SelectMany(item => item.Data)])];
        }

        private byte[] Meta(Dictionary<uint, uint> offsets)
        {
            var hdlr = FullBox("hdlr", 0, U32(0), Ascii("pict"), new byte[12], [0]);
            byte[] pitm = Primary is { } primary ? FullBox("pitm", 0, U16((int)primary)) : [];

            var infos = Items.Select(item => FullBox("infe", 2, U16((int)item.Id), U16(0), Ascii(item.Type), [0], item.ContentType is { } content ? [.. Ascii(content), 0, 0] : []));
            var iinf = FullBox("iinf", 0, U16(Items.Count), [.. infos.SelectMany(info => info)]);

            var iref = FullBox("iref", 0, [.. References.SelectMany(reference => 
                Box(reference.Type, U16((int)reference.From), U16(reference.To.Length), [.. reference.To.SelectMany(to => U16((int)to))]))]);

            var ipco = Box("ipco", [.. Properties.SelectMany(property => property)]);
            var ipma = FullBox("ipma", 0, U32((uint)Associations.Count), [.. Associations.SelectMany(association => 
                (byte[])[.. U16((int)association.Item), (byte)association.Properties.Length, .. association.Properties.Select(index => (byte)index)])]);

            var iloc = FullBox("iloc", 1, [0x44, 0x00], U16(Items.Count), [.. Items.SelectMany(item =>
                (byte[])[.. U16((int)item.Id), .. U16(item.Construction), .. U16(0), .. U16(1), .. U32(offsets.GetValueOrDefault(item.Id)), .. U32((uint)item.Data.Length)])]);

            return FullBox("meta", 0, hdlr, pitm, iinf, iref, Box("iprp", ipco, ipma), iloc);
        }
    }

    /// <summary>An Exif item's data: the offset to the TIFF, what the offset skips, then the TIFF.</summary>
    public static byte[] ExifItem(byte[] tiff, bool preamble = true) => preamble ? [.. U32(6), .. "Exif\0\0"u8, .. tiff] : [.. U32(0), .. tiff];

    // CR3

    private static readonly byte[] CanonUuid = [0x85, 0xC0, 0xB6, 0x87, 0x82, 0x0F, 0x11, 0xE0, 0x81, 0x11, 0xF4, 0xCE, 0x46, 0x2B, 0x6A, 0x48];
    private static readonly byte[] XmpUuid = [0xBE, 0x7A, 0xCF, 0xCB, 0x97, 0xA9, 0x42, 0xE8, 0x9C, 0x71, 0x99, 0x94, 0x91, 0xE3, 0xAF, 0xAC];

    /// <summary>Canon's metadata uuid box, holding the boxes given (CMT1 and the rest).</summary>
    public static byte[] Canon(params byte[][] boxes) => Box("uuid", CanonUuid, [.. boxes.SelectMany(box => box)]);

    /// <summary>A top-level XMP uuid box, NUL-padded as Canon writes it.</summary>
    public static byte[] XmpBox(string packet, int padding = 0) => Box("uuid", XmpUuid, Encoding.UTF8.GetBytes(packet), new byte[padding]);

    public static byte[] Cr3(byte[] moov, params byte[][] after) => [.. Ftyp("crx "), .. moov, .. after.SelectMany(box => box), .. Box("mdat", new byte[16])];
}
