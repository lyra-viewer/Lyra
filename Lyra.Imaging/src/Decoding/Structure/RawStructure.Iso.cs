using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Lyra.Common;

namespace Lyra.Imaging.Decoding.Structure;

internal static partial class RawStructure
{
    // CR3 and other ISO media files.

    private static class IsoParts
    {
        private const int MaxBoxes = 512;
        private const int MaxDepth = 6;

        private static readonly Dictionary<string, string> Uuids = new()
        {
            ["85c0b687820f11e08111f4ce462b6a48"] = "Canon metadata",
            ["eaf42b5e1c984b88b9fbb7dc406e4d16"] = "Canon preview",
            ["be7acfcb97a942e89c71999491e3afac"] = "XMP metadata",
            ["5766b829bb6a47c5bcfb8b9f2260d06d"] = "Canon timed metadata"
        };

        private static readonly Dictionary<string, string> Names = new()
        {
            ["ftyp"] = "File type",
            ["moov"] = "Movie",
            ["mvhd"] = "Movie header",
            ["trak"] = "Track",
            ["mdat"] = "Media data",
            ["free"] = "Free space",
            ["CCTP"] = "Track list",
            ["CMT1"] = "IFD0 (CMT1)",
            ["CMT2"] = "EXIF (CMT2)",
            ["CMT3"] = "Canon maker notes (CMT3)",
            ["CMT4"] = "GPS (CMT4)",
            ["THMB"] = "Thumbnail",
            ["PRVW"] = "Preview",
            ["CTBO"] = "Offset table",
            ["CNCV"] = "Compressor version"
        };

        private static readonly HashSet<string> Containers = ["moov", "trak", "mdia", "minf", "stbl", "dinf"];

        public static List<Node> Describe(Stream stream)
        {
            var top = new List<Node>();
            var trackData = new List<Node>();
            var count = 0;
            Walk(stream, 0, stream.Length, top, trackData, 0, ref count);
            top.AddRange(trackData);
            return top;
        }

        private static void Walk(Stream stream, long from, long to, List<Node> into, List<Node> trackData, int depth, ref int count)
        {
            var offset = from;
            var tracks = 0;

            while (offset + 8 <= to && count++ < MaxBoxes)
            {
                if (ReadAt(stream, offset, 8) is not { } header)
                    return;

                long size = BinaryPrimitives.ReadUInt32BigEndian(header);
                var type = Encoding.ASCII.GetString(header, 4, 4);
                var headerSize = 8;

                if (size == 1 && ReadAt(stream, offset + 8, 8) is { } large)
                {
                    size = BinaryPrimitives.ReadInt64BigEndian(large);
                    headerSize = 16;
                }
                else if (size == 0)
                {
                    size = to - offset;
                }

                if (size < headerSize || offset + size > to)
                    return;

                var name = Names.GetValueOrDefault(type, $"'{type}' box");
                string? description = null;
                var contentAt = offset + headerSize;

                if (type == "uuid" && ReadAt(stream, contentAt, 16) is { } uuid)
                {
                    var key = Convert.ToHexString(uuid).ToLowerInvariant();
                    name = Uuids.GetValueOrDefault(key, "Vendor box");
                    contentAt += 16;
                    description = $"uuid {key[..8]}...";
                }
                else if (type == "trak")
                {
                    name = $"Track {++tracks}";
                }
                else if (type == "ftyp" && ReadAt(stream, contentAt, 4) is { } brand)
                {
                    description = $"brand '{Encoding.ASCII.GetString(brand).Trim()}'";
                }

                var node = new Node(name, description ?? (type == "uuid" ? null : $"'{type}'"), offset, size);
                into.Add(node);
                BoxFields(stream, node, type, headerSize, offset + headerSize, offset + size);

                if (type == "trak" && depth < MaxDepth)
                {
                    // A track's boxes are bookkeeping; what matters is the data they point at.
                    var boxes = new List<Node>();
                    Walk(stream, contentAt, offset + size, boxes, trackData, depth + 1, ref count);
                    TrackData(stream, node, boxes, trackData);
                }
                else if (depth < MaxDepth)
                {
                    if (Containers.Contains(type) || (type == "uuid" && name == "Canon metadata"))
                        Walk(stream, contentAt, offset + size, node.Children, trackData, depth + 1, ref count);
                    else if (type == "uuid" && name == "Canon preview")
                        Walk(stream, contentAt + 8, offset + size, node.Children, trackData, depth + 1, ref count);
                }

                offset += size;
            }
        }

        /// <summary>What a box holds, as far as it is worth listing; every box at least its type and header. <paramref name="from"/> is just past the header, before a uuid's own 16 bytes.</summary>
        private static void BoxFields(Stream stream, Node node, string type, int headerSize, long from, long to)
        {
            node.Fields.Add(new("Box type", $"'{type}'"));
            node.Fields.Add(new("Header", $"{headerSize} B"));

            var length = to - from;
            uint U32(byte[] b, int at) => BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(at));

            switch (type)
            {
                case "uuid" when ReadAt(stream, from, 16) is { } uuid:
                    node.Fields.Add(new("UUID", Uuid(uuid)));
                    break;

                case "ftyp" when length >= 8 && ReadAt(stream, from, (int)Math.Min(length, 64)) is { } ftyp:
                    node.Fields.Add(new("Major brand", $"'{Encoding.ASCII.GetString(ftyp, 0, 4)}'"));
                    node.Fields.Add(new("Minor version", U32(ftyp, 4).ToString(CultureInfo.InvariantCulture)));
                    var brands = Enumerable.Range(0, (ftyp.Length - 8) / 4).Select(i => $"'{Encoding.ASCII.GetString(ftyp, 8 + i * 4, 4)}'");
                    node.Fields.Add(new("Compatible brands", string.Join(" ", brands)));
                    break;

                case "CNCV" when ReadAt(stream, from, (int)Math.Min(length, 64)) is { } version:
                    node.Fields.Add(new("Compressor version", Encoding.ASCII.GetString(version).TrimEnd('\0')));
                    break;

                case "CCTP" when ReadAt(stream, from, 12) is { } cctp:
                    var listed = U32(cctp, 8);
                    node.Fields.Add(new("Tracks listed", listed.ToString(CultureInfo.InvariantCulture)));

                    // One 24-byte CCDT per track: an image type, a dual pixel flag and the track's number.
                    for (var i = 0; i < Math.Min(listed, 8u); i++)
                    {
                        if (ReadAt(stream, from + 12 + i * 24L, 24) is not { } ccdt || !ccdt.AsSpan(4, 4).SequenceEqual("CCDT"u8))
                            break;

                        var imageType = BinaryPrimitives.ReadUInt64BigEndian(ccdt.AsSpan(8));
                        node.Fields.Add(new($"Track {U32(ccdt, 20)}", $"type 0x{imageType:X}{(U32(ccdt, 16) != 0 ? ", dual pixel" : "")}"));
                    }

                    break;

                case "CTBO" when ReadAt(stream, from, 4) is { } ctbo:
                    var count = U32(ctbo, 0);
                    node.Fields.Add(new("Entries", count.ToString(CultureInfo.InvariantCulture)));

                    for (var i = 0; i < Math.Min(count, 16u); i++)
                    {
                        if (ReadAt(stream, from + 4 + i * 20L, 20) is not { } item)
                            break;

                        var at = BinaryPrimitives.ReadInt64BigEndian(item.AsSpan(4));
                        var size = BinaryPrimitives.ReadInt64BigEndian(item.AsSpan(12));
                        var what = U32(item, 0) switch { 1 => "XMP", 2 => "preview", 3 => "media data", var n => $"entry {n}" };
                        node.Fields.Add(new($"Offset {U32(item, 0)} ({what})", at == 0 && size == 0 ? "unused" : $"{Hex(at)}, {Formatters.SizeToStr(size)}"));
                    }

                    break;

                case "CMT1" or "CMT2" or "CMT3" or "CMT4":
                    TiffParts.DescribeEmbedded(stream, from, type, node);
                    break;

                case "THMB" when ReadAt(stream, from, 12) is { } thmb:
                    node.Fields.Add(new("Width", $"{BinaryPrimitives.ReadUInt16BigEndian(thmb.AsSpan(4))} px"));
                    node.Fields.Add(new("Height", $"{BinaryPrimitives.ReadUInt16BigEndian(thmb.AsSpan(6))} px"));
                    node.Fields.Add(new("JPEG size", $"{U32(thmb, 8)} B"));
                    JpegFields(stream, node, from, to);
                    break;

                case "PRVW" when ReadAt(stream, from, 16) is { } prvw:
                    node.Fields.Add(new("Width", $"{BinaryPrimitives.ReadUInt16BigEndian(prvw.AsSpan(6))} px"));
                    node.Fields.Add(new("Height", $"{BinaryPrimitives.ReadUInt16BigEndian(prvw.AsSpan(8))} px"));
                    node.Fields.Add(new("JPEG size", $"{U32(prvw, 12)} B"));
                    JpegFields(stream, node, from, to);
                    break;

                case "mvhd" when ReadAt(stream, from, 32) is { } mvhd:
                    var longForm = mvhd[0] == 1;
                    var created = longForm ? (long)BinaryPrimitives.ReadUInt64BigEndian(mvhd.AsSpan(4)) : U32(mvhd, 4);
                    var timescale = U32(mvhd, longForm ? 20 : 12);
                    node.Fields.Add(new("Created", MediaTime(created)));
                    node.Fields.Add(new("Timescale", timescale.ToString(CultureInfo.InvariantCulture)));
                    break;

                case "trak":
                    break;

                case "mdat":
                    node.Fields.Add(new("Payload", $"{length} B"));
                    break;

                case "moov" or "free" or "skip":
                    break;

                default:
                    node.Fields.Add(new("Payload", $"{length} B"));
                    break;
            }
        }

        /// <summary>Where the JPEG inside a thumbnail or preview box starts, found by its marker, and its pixel size.</summary>
        private static void JpegFields(Stream stream, Node node, long from, long to)
        {
            if (ReadAt(stream, from, (int)Math.Min(64, to - from)) is not { } head)
                return;

            var jpeg = head.AsSpan().IndexOf(new byte[] { 0xFF, 0xD8 });
            if (jpeg < 0)
                return;

            node.Fields.Add(new("JPEG at", Hex(from + jpeg)));
            if (JpegFrame(stream, from + jpeg, to - from - jpeg) is { } frame)
                node.Fields.Add(new("JPEG frame", $"{frame.Width} x {frame.Height}"));
        }

        private static string Uuid(byte[] b)
        {
            var hex = Convert.ToHexString(b).ToLowerInvariant();
            return $"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..]}";
        }

        /// <summary>ISO media time: seconds since 1904, in UTC.</summary>
        private static string MediaTime(long seconds) => seconds is > 0 and < 8_000_000_000
            ? new DateTime(1904, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(seconds).ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture)
            : seconds.ToString(CultureInfo.InvariantCulture);

        /// <summary>Where a track's first sample sits in the media data, as its sample tables say.</summary>
        private static void TrackData(Stream stream, Node track, List<Node> boxes, List<Node> trackData)
        {
            var stbl = Find(boxes, "stbl");
            if (stbl is null)
                return;

            long? chunk = null, sample = null;
            string? format = null;
            int width = 0, height = 0;

            foreach (var box in stbl.Children)
            {
                var payload = box.Offset + 8;
                switch (box.Description)
                {
                    case "'co64'" when ReadAt(stream, payload + 8, 8) is { } c64:
                        chunk = BinaryPrimitives.ReadInt64BigEndian(c64);
                        break;
                    case "'stco'" when ReadAt(stream, payload + 8, 4) is { } c32:
                        chunk = BinaryPrimitives.ReadUInt32BigEndian(c32);
                        break;
                    case "'stsz'" when ReadAt(stream, payload + 4, 12) is { } sz:
                        var fixedSize = BinaryPrimitives.ReadUInt32BigEndian(sz);
                        sample = fixedSize != 0 ? fixedSize : BinaryPrimitives.ReadUInt32BigEndian(sz.AsSpan(8));
                        break;
                    case "'stsd'" when ReadAt(stream, payload + 8, 36) is { } entry:
                        format = Encoding.ASCII.GetString(entry, 4, 4);
                        width = BinaryPrimitives.ReadUInt16BigEndian(entry.AsSpan(32));
                        height = BinaryPrimitives.ReadUInt16BigEndian(entry.AsSpan(34));
                        break;
                }
            }

            if (Find(boxes, "tkhd") is { } tkhd && ReadAt(stream, tkhd.Offset + 8, 24) is { } header)
            {
                var longForm = header[0] == 1;
                var created = longForm ? (long)BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(4)) : BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(4));
                track.Fields.Add(new("Track ID", BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(longForm ? 20 : 12)).ToString(CultureInfo.InvariantCulture)));
                track.Fields.Add(new("Created", MediaTime(created)));
            }

            if (format is not null)
                track.Fields.Add(new("Format", width > 0 ? $"'{format}', {width} x {height}" : $"'{format}'"));

            if (chunk is not { } at || sample is not { } size || at <= 0 || at + size > stream.Length)
                return;

            track.Fields.Add(new("Data at", $"{Hex(at)}, {size} B"));

            if (ReadAt(stream, at, 2) is [0xFF, 0xD8])
            {
                trackData.Add(JpegNode(stream, at, size, track.Name));
                return;
            }

            var name = format switch
            {
                "CRAW" => width > 0 ? $"Raw image data {width} x {height}" : "Raw image data",
                "CTMD" => "Timed metadata",
                _ => $"Track data ('{format}')"
            };

            trackData.Add(new Node(name, track.Name, at, size));
        }

        private static Node? Find(List<Node> nodes, string type)
        {
            foreach (var node in nodes)
            {
                if (node.Description == $"'{type}'")
                    return node;

                if (Find(node.Children, type) is { } found)
                    return found;
            }

            return null;
        }
    }
}