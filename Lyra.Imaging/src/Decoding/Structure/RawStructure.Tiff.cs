using System.Globalization;
using System.Text;
using Lyra.Metadata.Exif;
using Lyra.Metadata.MakerNotes;
using Lyra.Metadata.Tiff;

namespace Lyra.Imaging.Decoding.Structure;

internal static partial class RawStructure
{
    // TIFF-based raws: DNG, NEF, ARW, CR2, PEF, SRW, ORF, RW2, 3FR, IIQ and the rest.

    private sealed class TiffParts
    {
        private const int MaxStrips = 100_000;

        private static readonly Dictionary<ushort, string> ExtraNames = new()
        {
            [330]   = "SubIFDs",
            [513]   = "JPEG Offset",
            [514]   = "JPEG Length",
            [34665] = "EXIF Offset",
            [34853] = "GPS Offset",
            [40965] = "Interoperability Offset",
            [37500] = "Maker Note"
        };

        private readonly Stream _stream;
        private readonly bool _little;
        private string? _make;
        private readonly List<Node> _top;
        private bool _panasonic;

        private const ushort PanasonicRawDataOffset = 0x118;
        
        private const int MakerNoteHeadLength = 32;

        private TiffParts(Stream stream, bool little, List<Node> top)
        {
            _stream = stream;
            _little = little;
            _top = top;
        }

        public static List<Node> Describe(Stream stream, byte[] head)
        {
            var little = head[0] == 'I';
            var magic = U16(head.AsSpan(2), little);
            var variant = magic switch
            {
                42 => "TIFF",
                0x4F52 or 0x5352 => "Olympus ORF",
                0x55 => "Panasonic RW2",
                _ => null
            };

            if (variant is null)
                return [];

            var next = (long)U32(head.AsSpan(4), little);

            var header = new Node($"{variant} header ({(little ? "Intel, little-endian" : "Motorola, big-endian")})", null, 0, 8);
            header.Fields.Add(new("Byte order", little ? "II (Intel, little-endian)" : "MM (Motorola, big-endian)"));
            header.Fields.Add(new("Magic number", $"{magic} (0x{magic:X4}, {variant})"));
            header.Fields.Add(new("First IFD at", Hex(next)));

            var top = new List<Node> { header };
            var parts = new TiffParts(stream, little, top) { _panasonic = magic == 0x55 };
            if (TiffDirectoryReader.Read(stream) is not { } layout)
                return top;

            for (var index = 0; index < layout.Directories.Count; index++)
            {
                var directory = layout.Directories[index];

                // Names the maker notes, which IFD0's EXIF leads to.
                if (index == 0 && directory.Find(271) is { } make && TiffDirectoryReader.ReadBytes(stream, make, 64) is { } text
                    && Encoding.ASCII.GetString(text).Split('\0')[0].Trim() is { Length: > 0 } maker)
                    parts._make = maker;

                var name = index switch
                {
                    0 => "Image (IFD0)",
                    1 when directory.Find(513) is not null => "Thumbnail (IFD1)",
                    _ => $"IFD{index}"
                };

                top.Add(parts.DirectoryNode(directory, name, name, depth: 0));
            }

            return top;
        }

        /// <summary>
        /// The tags of a TIFF kept whole inside another container (CR3's CMT boxes), whose offsets
        /// count from its own header at <paramref name="start"/>.
        /// </summary>
        public static void DescribeEmbedded(Stream stream, long start, string box, Node node)
        {
            if (ReadAt(stream, start, 8) is not { } head || !TiffDirectoryReader.IsTiff(head))
                return;

            var little = head[0] == 'I';
            var first = U32(head.AsSpan(4), little);
            node.Fields.Add(new("Byte order", little ? "II (Intel, little-endian)" : "MM (Motorola, big-endian)"));
            node.Fields.Add(new("Directory at", Hex(start + first)));

            if (TiffDirectoryReader.ReadDirectoryAt(stream, start + first, little, out _, start) is not { } directory)
                return;

            var kind = box switch { "CMT1" => TiffDirectoryKind.Image, "CMT2" => TiffDirectoryKind.Exif, "CMT4" => TiffDirectoryKind.Gps, _ => (TiffDirectoryKind?)null };
            var layout = new TiffParts(stream, little, []);

            foreach (var entry in directory.Entries)
                node.Fields.Add(new(box == "CMT3" ? MakerTagName(MakerNoteVendor.Canon, entry.Tag) : TagName(kind, entry.Tag), layout.Value(entry)));
        }

        private static string MakerTagName(MakerNoteVendor? vendor, ushort tag) =>
            vendor is { } known && MakerNoteTags.Name(known, tag) is { } name ? name : $"0x{tag:X4}";

        /// <summary>A directory, its entries, and the directories its pointers lead to as the engine read them.</summary>
        private Node DirectoryNode(TiffDirectory directory, string name, string path, int depth)
        {
            var count = directory.Entries.Count;
            var node = new Node(name, $"{count} {(count == 1 ? "entry" : "entries")}", directory.Offset, 2 + 12L * count + 4);
            var image = directory.Kind is TiffDirectoryKind.Image or TiffDirectoryKind.SubImage;
            var catalogKind = image ? TiffDirectoryKind.Image : directory.Kind;

            // RW2 puts its own tags in IFD0, some on numbers TIFF uses for other things.
            var panasonic = _panasonic && depth == 0;

            foreach (var entry in directory.Entries)
                node.Fields.Add(new(panasonic ? PanasonicTagName(entry.Tag) : TagName(catalogKind, entry.Tag), Value(entry)));

            var subIfds = 0;
            foreach (var child in directory.Children)
            {
                var childName = child.Kind switch
                {
                    TiffDirectoryKind.SubImage => $"SubIFD {++subIfds}",
                    TiffDirectoryKind.Exif => "EXIF",
                    TiffDirectoryKind.Gps => "GPS",
                    _ => "Interoperability"
                };

                node.Children.Add(DirectoryNode(child, childName, $"{path} / {childName}", depth + 1));
            }

            if (directory.Kind is TiffDirectoryKind.Exif && directory.Find(37500) is { } makerNote && MakerNoteNode(makerNote) is { } notes)
                node.Children.Add(notes);

            if (image)
                AddData(directory, path);

            return node;
        }

        /// <summary>What a directory's image data is: the raw data, a preview JPEG, or something else.</summary>
        private void AddData(TiffDirectory directory, string owner)
        {
            // An EXIF-style thumbnail.
            if (directory.Find(513) is { } jpegAt && directory.Find(514) is { } jpegLength
                                                  && Single(jpegAt) is { } at and > 0 && Single(jpegLength) is { } length and > 0
                                                  && at + length <= _stream.Length)
            {
                _top.Add(JpegNode(_stream, at, length, owner == "Thumbnail (IFD1)" ? "EXIF thumbnail" : $"{owner}, JPEG"));
            }

            // Strips or tiles; Panasonic gives only where its raw data starts, which runs to the end.
            var offsets = (_panasonic ? directory.Find(PanasonicRawDataOffset) : null) ?? directory.Find(273) ?? directory.Find(324);
            var counts = directory.Find(279) ?? directory.Find(325);
            if (offsets is { } o && TiffDirectoryReader.ReadIntegers(_stream, o, _little, MaxStrips) is { Length: > 0 } starts)
            {
                var sizes = counts is { } c ? TiffDirectoryReader.ReadIntegers(_stream, c, _little, MaxStrips) : null;
                var start = starts.Min();
                var total = sizes is not null && sizes.Length == starts.Length ? sizes.Sum() : 0;
                if (total == 0 && starts.Length == 1 && _panasonic)
                    total = _stream.Length - start;

                if (start > 0 && total > 0 && start < _stream.Length && start + total <= _stream.Length + total / 2)
                    _top.Add(ImageDataNode(directory, owner, start, Math.Min(total, _stream.Length - start), starts.Length, directory.Find(324) is not null));
            }

            // A JPEG kept whole in a tag, as RW2 (JpgFromRaw) and some others do.
            foreach (var entry in directory.Entries)
            {
                if (entry.Type is not (TiffFieldType.Byte or TiffFieldType.Undefined) || entry.ValueSize < 1024 || entry.Tag is 37500)
                    continue;

                if (ReadAt(_stream, entry.ValueOffset, 2) is [0xFF, 0xD8])
                    _top.Add(JpegNode(_stream, entry.ValueOffset, entry.ValueSize, _panasonic && entry.Tag == 0x2E ? $"{owner}, JpgFromRaw" : $"{owner}, tag 0x{entry.Tag:X4}"));
            }
        }

        private Node ImageDataNode(TiffDirectory directory, string owner, long start, long size, int pieces, bool tiled)
        {
            var width = Single(directory.Find(256)) ?? (_panasonic ? Single(directory.Find(2)) : null);
            var height = Single(directory.Find(257)) ?? (_panasonic ? Single(directory.Find(3)) : null);
            var compression = Single(directory.Find(259)) ?? (_panasonic ? Single(directory.Find(0x0B)) : null);
            var photometric = Single(directory.Find(262));
            var samples = Single(directory.Find(277));
            var bits = directory.Find(258) is { } b && TiffDirectoryReader.ReadIntegers(_stream, b, _little, 8) is [var first, ..]
                ? first
                : _panasonic
                    ? Single(directory.Find(10))
                    : null;

            var jpeg = ReadAt(_stream, start, 2) is [0xFF, 0xD8] ? JpegFrame(_stream, start, size) : null;
            var raw = photometric is 32803 or 34892
                      || jpeg is { Lossless: true }
                      || compression is 34713 or 32767 or 65535 or 32769 or 32770 or 32772 or 65000
                      || (_panasonic && directory.Find(PanasonicRawDataOffset) is not null)
                      || directory.Find(33422) is not null
                      || (samples is null or 1 && photometric is not (2 or 6) && bits >= 10);

            var dimensions = width is { } w && height is { } h ? $" {w} x {h}" : "";
            var name = raw ? $"Raw image data{dimensions}"
                : jpeg is { } frame ? $"Embedded JPEG {frame.Width} x {frame.Height}"
                : $"Image data{dimensions}";

            var coding = jpeg is { Lossless: true } ? "lossless JPEG" : Compression(compression);
            var node = new Node(name, $"{owner}, {coding}", start, size);
            
            if (width is { } fw) 
                node.Fields.Add(new("Width", $"{fw} px"));
            
            if (height is { } fh) 
                node.Fields.Add(new("Height", $"{fh} px"));
            
            if (bits is { } fb) 
                node.Fields.Add(new("Bits per sample", fb.ToString(CultureInfo.InvariantCulture)));
            
            node.Fields.Add(new("Compression", coding));
            
            if (photometric is { } p) 
                node.Fields.Add(new("Photometric", Photometric(p)));
            
            node.Fields.Add(new(tiled ? "Tiles" : "Strips", pieces.ToString(CultureInfo.InvariantCulture)));

            return node;
        }

        private long? Single(TiffEntry? entry) => entry is { } e && TiffDirectoryReader.ReadIntegers(_stream, e, _little, 1) is [var value] ? value : null;
        
        /// <summary>The camera's private directory, where <see cref="MakerNote"/> finds it; described as unread when it does not look like one.</summary>
        private Node? MakerNoteNode(TiffEntry entry)
        {
            if (entry.ValueSize <= 0 || ReadAt(_stream, entry.ValueOffset, (int)Math.Min(entry.ValueSize, MakerNoteHeadLength)) is not { } head)
                return null;

            var start = entry.ValueOffset;
            var note = MakerNote.Locate(head, _make, _little);
            var ifd = start + note.DirectoryOffset;
            var baseOffset = note.BaseOffset is { } relative ? start + relative : 0;
            var name = $"{_make ?? "Camera"} maker notes";

            if (MakerNote.LooksLikeDirectory(_stream, ifd, note.LittleEndian)
                && TiffDirectoryReader.ReadDirectoryAt(_stream, ifd, note.LittleEndian, out _, baseOffset) is { } directory)
            {
                var node = new Node(name, $"{directory.Entries.Count} entries", start, entry.ValueSize);

                node.Fields.Add(new("Byte order", note.LittleEndian ? "Intel, little-endian" : "Motorola, big-endian"));
                node.Fields.Add(new("Directory at", Hex(ifd)));
                if (baseOffset != 0)
                    node.Fields.Add(new("Offsets count from", Hex(baseOffset)));

                foreach (var e in directory.Entries)
                    node.Fields.Add(new(MakerTagName(note.Vendor, e.Tag), Value(e, note.LittleEndian)));

                return node;
            }

            return new Node(name, $"{entry.ValueSize} bytes in a form Lyra does not read", start, entry.ValueSize);
        }

        private static string PanasonicTagName(ushort tag) => TagCatalog.PanasonicRawName(tag) ?? $"Tag 0x{tag:X4}";

        private static string TagName(TiffDirectoryKind? kind, ushort tag)
        {
            if (kind is { } known && TagCatalog.Name(known, tag) is { } name)
                return name;

            if (ExtraNames.TryGetValue(tag, out var extra))
                return extra;

            return TagCatalog.Name(TiffDirectoryKind.Image, tag) ?? $"Tag 0x{tag:X4}";
        }

        private string Value(TiffEntry entry) => Value(entry, _little);

        private string Value(TiffEntry entry, bool little)
        {
            switch (entry.Type)
            {
                case TiffFieldType.Ascii:
                    return ReadAt(_stream, entry.ValueOffset, (int)Math.Min(entry.ValueSize, 128)) is { } text
                        ? Text(text, truncated: entry.ValueSize > 128)
                        : $"{entry.Count} characters";

                // Versions and the like: a few printable characters.
                case TiffFieldType.Undefined when entry.ValueSize <= 16 && ReadAt(_stream, entry.ValueOffset, (int)entry.ValueSize) is { } bytes && bytes.All(b => b is >= 0x20 and < 0x7F):
                    return Encoding.ASCII.GetString(bytes);

                case TiffFieldType.Byte or TiffFieldType.Short or TiffFieldType.Long or TiffFieldType.SByte or TiffFieldType.SShort or TiffFieldType.SLong or TiffFieldType.Ifd
                    when entry.Count <= 8:
                    return TiffDirectoryReader.ReadIntegers(_stream, entry, little, 8) is { } values
                        ? string.Join(" ", values.Select(v => v.ToString(CultureInfo.InvariantCulture)))
                        : "?";

                case TiffFieldType.Rational or TiffFieldType.SRational when entry.Count <= 4:
                    return Rationals(entry, little);

                case TiffFieldType.Float when entry.Count <= 4:
                    return TiffDirectoryReader.ReadFloats(_stream, entry, little, 4) is { } floats
                        ? string.Join(" ", floats.Select(f => f.ToString("0.####", CultureInfo.InvariantCulture)))
                        : "?";

                case TiffFieldType.Byte or TiffFieldType.Undefined:
                    return $"{entry.ValueSize} bytes";

                default:
                    return $"{entry.Count} values";
            }
        }

        private string Rationals(TiffEntry entry, bool little) =>
            TiffDirectoryReader.ReadRationals(_stream, entry, little, 4) is { } rationals
                ? string.Join(" ", rationals.Select(r => r.IsDefined ? ((double)r.Numerator / r.Denominator).ToString("0.####", CultureInfo.InvariantCulture) : "-"))
                : "?";

        private static string Text(byte[] bytes, bool truncated)
        {
            var text = Encoding.ASCII.GetString(bytes).Split('\0')[0].Trim();
            return text.Length == 0 ? "(empty)" : truncated ? text + "..." : text;
        }

        private static string Compression(long? value) => value switch
        {
            null  => "unknown",
            1     => "uncompressed",
            6     => "JPEG (old-style)",
            7     => "JPEG",
            8 or 32946 => "Deflate",
            34713 => "Nikon NEF",
            34892 => "lossy JPEG (DNG)",
            32767 => "Sony ARW",
            52546 => "JPEG XL (DNG)",
            65000 => "Kodak",
            65535 => "Pentax PEF",
            34316 => "Panasonic",
            32769 or 32770 or 32772 => "Samsung",
            32773 => "PackBits",
            _ => value.Value.ToString(CultureInfo.InvariantCulture)
        };

        private static string Photometric(long value) => value switch
        {
            0     => "white is zero",
            1     => "black is zero",
            2     => "RGB",
            6     => "YCbCr",
            32803 => "CFA (colour filter array)",
            34892 => "linear raw",
            _ => value.ToString(CultureInfo.InvariantCulture)
        };
    }
}