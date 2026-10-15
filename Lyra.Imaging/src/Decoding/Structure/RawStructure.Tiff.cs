using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Lyra.ManagedCodecs.Raster.Tiff;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Exif.Makernotes;
using Directory = MetadataExtractor.Directory;

namespace Lyra.Imaging.Decoding.Structure;

internal static partial class RawStructure
{
    // TIFF-based raws: DNG, NEF, ARW, CR2, PEF, SRW, ORF, RW2, 3FR, IIQ and the rest.

    private sealed class TiffParts
    {
        private const int MaxDepth = 6;
        private const int MaxStrips = 100_000;

        private enum Kind
        {
            Ifd,
            Exif,
            Gps,
            Interop,
            MakerNote
        }

        private static readonly Directory Ifd0Names = new ExifIfd0Directory();
        private static readonly Directory ExifNames = new ExifSubIfdDirectory();
        private static readonly Directory GpsNames = new GpsDirectory();
        private static readonly Directory InteropNames = new ExifInteropDirectory();
        private static readonly Directory[] FallbackNames = [new ExifSubIfdDirectory(), new ExifThumbnailDirectory(0), new ExifIfd0Directory()];

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
        private string _make = "Camera";
        private readonly List<Node> _top;
        private readonly HashSet<long> _visited = [];
        private bool _panasonic;

        private const ushort PanasonicRawDataOffset = 0x118;

        private TiffParts(Stream stream, bool little, List<Node> top)
        {
            _stream = stream;
            _little = little;
            _top = top;
        }

        public static List<Node> Describe(Stream stream, byte[] head)
        {
            var little = head[0] == 'I';
            var magic = little ? BinaryPrimitives.ReadUInt16LittleEndian(head.AsSpan(2)) : BinaryPrimitives.ReadUInt16BigEndian(head.AsSpan(2));
            var variant = magic switch
            {
                42 => "TIFF",
                0x4F52 or 0x5352 => "Olympus ORF",
                0x55 => "Panasonic RW2",
                _ => null
            };

            if (variant is null)
                return [];

            var next = (long)(little ? BinaryPrimitives.ReadUInt32LittleEndian(head.AsSpan(4)) : BinaryPrimitives.ReadUInt32BigEndian(head.AsSpan(4)));

            var header = new Node($"{variant} header ({(little ? "Intel, little-endian" : "Motorola, big-endian")})", null, 0, 8);
            header.Fields.Add(new("Byte order", little ? "II (Intel, little-endian)" : "MM (Motorola, big-endian)"));
            header.Fields.Add(new("Magic number", $"{magic} (0x{magic:X4}, {variant})"));
            header.Fields.Add(new("First IFD at", Hex(next)));

            var top = new List<Node> { header };
            var layout = new TiffParts(stream, little, top) { _panasonic = magic == 0x55 };

            for (var index = 0; next != 0 && index < 64; index++)
            {
                if (!layout._visited.Add(next) || TiffDirectoryReader.ReadDirectoryAt(stream, next, little, out var following) is not { } directory)
                    break;

                // Names the maker notes, which IFD0's EXIF leads to.
                if (index == 0 && directory.Find(271) is { } make && TiffDirectoryReader.ReadBytes(stream, make, 64) is { } text
                    && Encoding.ASCII.GetString(text).Split('\0')[0].Trim() is { Length: > 0 } maker)
                    layout._make = maker;

                var name = index switch
                {
                    0 => "Image (IFD0)",
                    1 when directory.Find(513) is not null => "Thumbnail (IFD1)",
                    _ => $"IFD{index}"
                };

                top.Add(layout.DirectoryNode(directory, name, name, Kind.Ifd, 0, 0));
                next = following;
            }

            return top;
        }

        /// <summary>
        /// The tags of a TIFF kept whole inside another container (CR3's CMT boxes), whose offsets
        /// count from its own header at <paramref name="start"/>.
        /// </summary>
        public static void DescribeEmbedded(Stream stream, long start, string box, Node node)
        {
            if (ReadAt(stream, start, 8) is not { } head || !IsTiff(head))
                return;

            var little = head[0] == 'I';
            var first = little ? BinaryPrimitives.ReadUInt32LittleEndian(head.AsSpan(4)) : BinaryPrimitives.ReadUInt32BigEndian(head.AsSpan(4));
            node.Fields.Add(new("Byte order", little ? "II (Intel, little-endian)" : "MM (Motorola, big-endian)"));
            node.Fields.Add(new("Directory at", Hex(start + first)));

            if (TiffDirectoryReader.ReadDirectoryAt(stream, start + first, little, out _, start) is not { } directory)
                return;

            var names = box switch { "CMT1" => Ifd0Names, "CMT2" => ExifNames, "CMT3" => MakerTables.Canon, "CMT4" => GpsNames, _ => null };
            var layout = new TiffParts(stream, little, []);

            foreach (var entry in directory.Entries)
                node.Fields.Add(new(box == "CMT3" ? MakerTagName(names, entry.Tag) : TagName(names, entry.Tag), layout.Value(entry)));
        }

        private static string MakerTagName(Directory? names, ushort tag) => names is not null && names.HasTagName(tag) ? names.GetTagName(tag) : $"0x{tag:X4}";

        private Node DirectoryNode(TiffDirectory directory, string name, string path, Kind kind, long baseOffset, int depth)
        {
            var count = directory.Entries.Count;
            var node = new Node(name, $"{count} {(count == 1 ? "entry" : "entries")}", directory.Offset, 2 + 12L * count + 4);
            var names = kind switch { Kind.Exif => ExifNames, Kind.Gps => GpsNames, Kind.Interop => InteropNames, Kind.Ifd => Ifd0Names, _ => null };

            // RW2 puts its own tags in IFD0, some on numbers TIFF uses for other things.
            var panasonic = _panasonic && depth == 0 && kind is Kind.Ifd;

            foreach (var entry in directory.Entries)
                node.Fields.Add(new(panasonic ? PanasonicTagName(entry.Tag) : TagName(names, entry.Tag), Value(entry)));

            if (depth >= MaxDepth)
                return node;

            if (kind is Kind.Ifd)
            {
                if (directory.Find(TiffDirectoryReader.TagSubIfds) is { } subIfds && Pointers(subIfds) is { } pointers)
                    for (var i = 0; i < pointers.Length; i++)
                        AddDirectory(node, path, baseOffset + pointers[i], $"SubIFD {i + 1}", Kind.Ifd, baseOffset, depth);

                if (directory.Find(34665) is { } exif && Pointers(exif) is [var exifAt, ..])
                    AddDirectory(node, path, baseOffset + exifAt, "EXIF", Kind.Exif, baseOffset, depth);

                if (directory.Find(34853) is { } gps && Pointers(gps) is [var gpsAt, ..])
                    AddDirectory(node, path, baseOffset + gpsAt, "GPS", Kind.Gps, baseOffset, depth);
            }

            if (kind is Kind.Exif)
            {
                if (directory.Find(40965) is { } interop && Pointers(interop) is [var interopAt, ..])
                    AddDirectory(node, path, baseOffset + interopAt, "Interoperability", Kind.Interop, baseOffset, depth);

                if (directory.Find(37500) is { } makerNote && MakerNoteNode(makerNote) is { } notes)
                    node.Children.Add(notes);
            }

            if (kind is Kind.Ifd)
                AddData(directory, path, baseOffset);

            return node;
        }

        private void AddDirectory(Node parent, string parentPath, long offset, string name, Kind kind, long baseOffset, int depth)
        {
            if (!_visited.Add(offset) || TiffDirectoryReader.ReadDirectoryAt(_stream, offset, _little, out _, baseOffset) is not { } directory)
                return;

            parent.Children.Add(DirectoryNode(directory, name, $"{parentPath} / {name}", kind, baseOffset, depth + 1));
        }

        private long[]? Pointers(TiffEntry entry) =>
            TiffDirectoryReader.ReadIntegers(_stream, entry, _little, 16) is { Length: > 0 } values && values.All(v => v > 0) ? values : null;

        /// <summary>What a directory's image data is: the raw data, a preview JPEG, or something else.</summary>
        private void AddData(TiffDirectory directory, string owner, long baseOffset)
        {
            // An EXIF-style thumbnail.
            if (directory.Find(513) is { } jpegAt && directory.Find(514) is { } jpegLength
                                                  && Single(jpegAt) is { } at and > 0 && Single(jpegLength) is { } length and > 0
                                                  && baseOffset + at + length <= _stream.Length)
            {
                _top.Add(JpegNode(_stream, baseOffset + at, length, owner == "Thumbnail (IFD1)" ? "EXIF thumbnail" : $"{owner}, JPEG"));
            }

            // Strips or tiles; Panasonic gives only where its raw data starts, which runs to the end.
            var offsets = (_panasonic ? directory.Find(PanasonicRawDataOffset) : null) ?? directory.Find(273) ?? directory.Find(324);
            var counts = directory.Find(279) ?? directory.Find(325);
            if (offsets is { } o && TiffDirectoryReader.ReadIntegers(_stream, o, _little, MaxStrips) is { Length: > 0 } starts)
            {
                var sizes = counts is { } c ? TiffDirectoryReader.ReadIntegers(_stream, c, _little, MaxStrips) : null;
                var start = baseOffset + starts.Min();
                var total = sizes is not null && sizes.Length == starts.Length ? sizes.Sum() : 0;
                if (total == 0 && starts.Length == 1 && _panasonic)
                    total = _stream.Length - start;

                if (start > 0 && total > 0 && start < _stream.Length && start + total <= _stream.Length + total / 2)
                    _top.Add(ImageDataNode(directory, owner, start, Math.Min(total, _stream.Length - start), starts.Length, directory.Find(324) is not null));
            }

            // A JPEG kept whole in a tag, as RW2 (JpgFromRaw) and some others do.
            foreach (var entry in directory.Entries)
            {
                if (entry.Type is not (1 or 7) || entry.ValueSize < 1024 || entry.Tag is 37500)
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
        
        /// <summary>
        /// The camera's private directory. Most are a plain IFD; some put a header in front and
        /// count offsets from the note itself, or carry a whole TIFF of their own.
        /// </summary>
        private Node? MakerNoteNode(TiffEntry entry)
        {
            if (entry.ValueSize <= 0 || ReadAt(_stream, entry.ValueOffset, (int)Math.Min(entry.ValueSize, 32)) is not { } head)
                return null;

            var start = entry.ValueOffset;
            var little = _little;
            long ifd, baseOffset = 0;

            bool Starts(ReadOnlySpan<byte> prefix) => head.AsSpan().StartsWith(prefix);

            if (Starts("Nikon\0"u8) && head.Length >= 18)
            {
                baseOffset = start + 10;
                little = head[10] == 'I';
                ifd = baseOffset + (little ? BinaryPrimitives.ReadUInt32LittleEndian(head.AsSpan(14)) : BinaryPrimitives.ReadUInt32BigEndian(head.AsSpan(14)));
            }
            else if (Starts("OLYMPUS\0"u8) && head.Length >= 12)
            {
                baseOffset = start;
                little = head[8] == 'I';
                ifd = start + 12;
            }
            else if (Starts("OM SYSTEM\0"u8) && head.Length >= 16)
            {
                baseOffset = start;
                little = head[12] == 'I';
                ifd = start + 16;
            }
            else if (Starts("Apple iOS\0"u8))
            {
                baseOffset = start;
                little = false;
                ifd = start + 14;
            }
            else if (Starts("FUJIFILM"u8) && head.Length >= 12)
            {
                baseOffset = start;
                little = true;
                ifd = start + BinaryPrimitives.ReadUInt32LittleEndian(head.AsSpan(8));
            }
            else if (Starts("PENTAX \0"u8) && head.Length >= 10)
            {
                baseOffset = start;
                little = head[8] == 'I';
                ifd = start + 10;
            }
            else if (Starts("AOC\0"u8) && head.Length >= 6)
            {
                little = head[4] == 'I' || (head[4] == ' ' && _little);
                ifd = start + 6;
            }
            else if (Starts("Panasonic\0"u8) || Starts("SONY DSC \0"u8) || Starts("SONY CAM \0"u8))
            {
                ifd = start + 12;
            }
            else if (Starts("OLYMP\0"u8) || Starts("LEICA\0"u8) || Starts("SIGMA\0"u8))
            {
                ifd = start + 8;
            }
            else
            {
                ifd = start;
            }

            var name = $"{_make} maker notes";

            if (LooksLikeDirectory(ifd, little) && TiffDirectoryReader.ReadDirectoryAt(_stream, ifd, little, out _, baseOffset) is { } directory)
            {
                var node = new Node(name, $"{directory.Entries.Count} entries", start, entry.ValueSize);
                var names = MakerNames(head);

                node.Fields.Add(new("Byte order", little ? "Intel, little-endian" : "Motorola, big-endian"));
                node.Fields.Add(new("Directory at", Hex(ifd)));
                if (baseOffset != 0)
                    node.Fields.Add(new("Offsets count from", Hex(baseOffset)));

                foreach (var e in directory.Entries)
                    node.Fields.Add(new(MakerTagName(names, e.Tag), Value(e, little)));

                return node;
            }

            return new Node(name, $"{entry.ValueSize} bytes in a form Lyra does not read", start, entry.ValueSize);
        }

        /// <summary>The camera maker's tag names, as MetadataExtractor knows them; null for a maker it does not.</summary>
        private Directory? MakerNames(byte[] head)
        {
            var span = head.AsSpan();
            if (span.StartsWith("AOC\0"u8) || span.StartsWith("PENTAX \0"u8))
                return MakerTables.Pentax;

            var make = _make.ToUpperInvariant();
            return make switch
            {
                _ when make.StartsWith("CANON") => MakerTables.Canon,
                _ when make.StartsWith("NIKON") => MakerTables.Nikon,
                _ when make.StartsWith("SONY") => MakerTables.Sony,
                _ when make.StartsWith("OLYMPUS") || make.StartsWith("OM DIGITAL") => MakerTables.Olympus,
                _ when make.StartsWith("PANASONIC") => MakerTables.Panasonic,
                _ when make.StartsWith("FUJI") => MakerTables.Fujifilm,
                _ when make.StartsWith("APPLE") => MakerTables.Apple,
                _ when make.StartsWith("SAMSUNG") => MakerTables.Samsung,
                _ when make.StartsWith("LEICA") => MakerTables.Leica,
                _ when make.StartsWith("PENTAX") || make.StartsWith("RICOH") => MakerTables.Pentax,
                _ when make.StartsWith("SIGMA") => MakerTables.Sigma,
                _ => null
            };
        }

        /// <summary>A directory's entry count is sane and its entries mostly carry real TIFF types.</summary>
        private bool LooksLikeDirectory(long offset, bool little)
        {
            if (ReadAt(_stream, offset, 2) is not { } countBytes)
                return false;

            var count = little ? BinaryPrimitives.ReadUInt16LittleEndian(countBytes) : BinaryPrimitives.ReadUInt16BigEndian(countBytes);
            if (count is 0 or > 1000 || ReadAt(_stream, offset + 2, Math.Min(count, (ushort)16) * 12) is not { } entries)
                return false;

            var known = 0;
            for (var i = 0; i < entries.Length / 12; i++)
            {
                var type = little ? BinaryPrimitives.ReadUInt16LittleEndian(entries.AsSpan(i * 12 + 2)) : BinaryPrimitives.ReadUInt16BigEndian(entries.AsSpan(i * 12 + 2));
                if (type is >= 1 and <= 13)
                    known++;
            }

            return known * 5 >= entries.Length / 12 * 4;
        }

        private static class MakerTables
        {
            public static readonly Directory Canon = new CanonMakernoteDirectory();
            public static readonly Directory Nikon = new NikonType2MakernoteDirectory();
            public static readonly Directory Sony = new SonyType1MakernoteDirectory();
            public static readonly Directory Olympus = new OlympusMakernoteDirectory();
            public static readonly Directory Panasonic = new PanasonicMakernoteDirectory();
            public static readonly Directory Fujifilm = new FujifilmMakernoteDirectory();
            public static readonly Directory Apple = new AppleMakernoteDirectory();
            public static readonly Directory Samsung = new SamsungType2MakernoteDirectory();
            public static readonly Directory Leica = new LeicaMakernoteDirectory();
            public static readonly Directory Pentax = new PentaxMakernoteDirectory();
            public static readonly Directory Sigma = new SigmaMakernoteDirectory();
        }

        private static readonly Dictionary<ushort, string> PanasonicNames = new()
        {
            [0x01]   = "Panasonic Raw Version",
            [0x02]   = "Sensor Width",
            [0x03]   = "Sensor Height",
            [0x04]   = "Sensor Top Border",
            [0x05]   = "Sensor Left Border",
            [0x06]   = "Sensor Bottom Border",
            [0x07]   = "Sensor Right Border",
            [0x09]   = "CFA Pattern",
            [0x0A]   = "Bits Per Sample",
            [0x0B]   = "Compression",
            [0x0E]   = "Linearity Limit Red",
            [0x0F]   = "Linearity Limit Green",
            [0x10]   = "Linearity Limit Blue",
            [0x17]   = "ISO",
            [0x18]   = "High ISO Multiplier Red",
            [0x19]   = "High ISO Multiplier Green",
            [0x1A]   = "High ISO Multiplier Blue",
            [0x1C]   = "Black Level Red",
            [0x1D]   = "Black Level Green",
            [0x1E]   = "Black Level Blue",
            [0x24]   = "WB Red Level",
            [0x25]   = "WB Green Level",
            [0x26]   = "WB Blue Level",
            [0x2E]   = "JpgFromRaw",
            [0x2F]   = "Crop Top",
            [0x30]   = "Crop Left",
            [0x31]   = "Crop Bottom",
            [0x32]   = "Crop Right",
            [0x10F]  = "Make",
            [0x110]  = "Model",
            [0x111]  = "Strip Offsets",
            [0x112]  = "Orientation",
            [0x116]  = "Rows Per Strip",
            [0x117]  = "Strip Byte Counts",
            [0x118]  = "Raw Data Offset",
            [0x119]  = "Distortion Info",
            [0x11C]  = "Gamma",
            [0x8769] = "EXIF Offset",
            [0x8825] = "GPS Offset"
        };

        private static string PanasonicTagName(ushort tag) => PanasonicNames.GetValueOrDefault(tag, $"Tag 0x{tag:X4}");

        private static string TagName(Directory? names, ushort tag)
        {
            if (names is not null && names.HasTagName(tag))
                return names.GetTagName(tag);

            if (ExtraNames.TryGetValue(tag, out var extra))
                return extra;

            foreach (var fallback in FallbackNames)
                if (fallback.HasTagName(tag))
                    return fallback.GetTagName(tag);

            return $"Tag 0x{tag:X4}";
        }

        private string Value(TiffEntry entry) => Value(entry, _little);

        private string Value(TiffEntry entry, bool little)
        {
            switch (entry.Type)
            {
                case 2:
                    return ReadAt(_stream, entry.ValueOffset, (int)Math.Min(entry.ValueSize, 128)) is { } text
                        ? Text(text, truncated: entry.ValueSize > 128)
                        : $"{entry.Count} characters";

                // Versions and the like: a few printable characters.
                case 7 when entry.ValueSize <= 16 && ReadAt(_stream, entry.ValueOffset, (int)entry.ValueSize) is { } bytes && bytes.All(b => b is >= 0x20 and < 0x7F):
                    return Encoding.ASCII.GetString(bytes);

                case 1 or 3 or 4 or 6 or 8 or 9 or 13 when entry.Count <= 8:
                    return TiffDirectoryReader.ReadIntegers(_stream, entry, little, 8) is { } values
                        ? string.Join(" ", values.Select(v => v.ToString(CultureInfo.InvariantCulture)))
                        : "?";

                case 5 or 10 when entry.Count <= 4:
                    return Rationals(entry, little);

                case 11 when entry.Count <= 4:
                    return TiffDirectoryReader.ReadFloats(_stream, entry, little, 4) is { } floats
                        ? string.Join(" ", floats.Select(f => f.ToString("0.####", CultureInfo.InvariantCulture)))
                        : "?";

                case 1 or 7:
                    return $"{entry.ValueSize} bytes";

                default:
                    return $"{entry.Count} values";
            }
        }

        private string Rationals(TiffEntry entry, bool little)
        {
            if (ReadAt(_stream, entry.ValueOffset, (int)entry.ValueSize) is not { } bytes)
                return "?";

            var parts = new List<string>();
            for (var i = 0; i + 8 <= bytes.Length; i += 8)
            {
                double numerator = entry.Type == 10
                    ? (little ? BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(i)) : BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(i)))
                    : (little ? BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(i)) : BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(i)));
                double denominator = entry.Type == 10
                    ? (little ? BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(i + 4)) : BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(i + 4)))
                    : (little ? BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(i + 4)) : BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(i + 4)));

                parts.Add(denominator == 0 ? "-" : (numerator / denominator).ToString("0.####", CultureInfo.InvariantCulture));
            }

            return string.Join(" ", parts);
        }

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