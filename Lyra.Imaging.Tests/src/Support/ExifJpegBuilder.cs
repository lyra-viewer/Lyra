using System.Text;
using SkiaSharp;

namespace Lyra.Imaging.Tests.Support;

/// <summary>
/// Builds JPEGs carrying a handwritten EXIF block, so metadata tests can state the exact tag
/// values they depend on instead of relying on committed camera files.
///
/// The block is a minimal big-endian TIFF spliced into an APP1 segment after SOI:
///
///   IFD0    Orientation (0x0112), DateTime (0x0132), ExifOffset (0x8769)
///   SubIFD  DateTimeOriginal (0x9003), DateTimeDigitized (0x9004),
///           ExposureBias (0x9204), ColorSpace (0xA001)
///
/// Optional tags are omitted entirely when not requested, and offsets are computed from the
/// entries actually written - so a test can leave out DateTimeOriginal to exercise a fallback
/// without the layout going stale. Tags are emitted in ascending order, as the spec requires.
/// </summary>
internal static class ExifJpegBuilder
{
    public const int Width = 16;
    public const int Height = 8;

    /// <summary>Writes a temp JPEG and returns its path. The caller deletes it.</summary>
    public static string Write(
        ushort orientation = 1,
        string? dateTimeOriginal = "2024:09:26 18:32:17",
        string? dateTimeDigitized = null,
        string? dateTime = null,
        ushort colorSpace = 0xFFFF,
        ExposureBias? exposureBias = null,
        bool wideGamut = false,
        string? artist = null,
        XmpFields? xmp = null,
        IptcFields? iptc = null)
    {
        var ifd0 = new List<Entry> { Entry.Short(0x0112, orientation) };
        if (dateTime is not null)
            ifd0.Add(Entry.Ascii(0x0132, dateTime));
        
        if (artist is not null)
            ifd0.Add(Entry.Ascii(0x013B, artist));

        var subIfd = new List<Entry>();
        if (dateTimeOriginal is not null)
            subIfd.Add(Entry.Ascii(0x9003, dateTimeOriginal));
        
        if (dateTimeDigitized is not null)
            subIfd.Add(Entry.Ascii(0x9004, dateTimeDigitized));
        
        if (exposureBias is { } bias)
            subIfd.Add(Entry.SignedRational(0x9204, bias.Numerator, bias.Denominator));
        
        subIfd.Add(Entry.Short(0xA001, colorSpace));

        byte[] exifIdentifier = [0x45, 0x78, 0x69, 0x66, 0x00, 0x00]; // "Exif\0\0"
        var segments = new List<(byte Marker, byte[] Payload)> { (0xE1, [.. exifIdentifier, .. BuildExifPayload(ifd0, subIfd)]) };
        
        if (xmp is not null)
            segments.Add((0xE1, BuildXmpSegment(xmp)));
        
        if (iptc is not null)
            segments.Add((0xED, BuildIptcSegment(iptc)));

        var path = Path.Combine(Path.GetTempPath(), $"lyra-exif-{Guid.NewGuid():N}.jpg");
        File.WriteAllBytes(path, InsertSegments(EncodeJpeg(wideGamut), segments));
        return path;
    }

    internal sealed record ExposureBias(
        int Numerator,
        int Denominator
    );

    internal sealed record XmpFields(
        string? Title = null,
        string? Description = null,
        string[]? Keywords = null,
        int? Rating = null,
        string? Creator = null,
        string? Rights = null
    );

    internal sealed record IptcFields(
        string? ObjectName = null,
        string? Caption = null,
        string[]? Keywords = null,
        string? ByLine = null,
        string? CopyrightNotice = null
    );

    /// <summary>
    /// Red on the left half, blue on the right - asymmetric enough to spot a rotation.
    /// With <paramref name="wideGamut"/> the bitmap is tagged Display-P3, which makes Skia's
    /// JPEG encoder embed a real ICC profile in an APP2 segment.
    /// </summary>
    private static byte[] EncodeJpeg(bool wideGamut)
    {
        var colorSpace = wideGamut
            ? SKColorSpace.CreateRgb(SKColorSpaceTransferFn.Srgb, SKColorSpaceXyz.DisplayP3)
            : null;

        using var bitmap = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Opaque, colorSpace));

        for (var y = 0; y < Height; y++)
        for (var x = 0; x < Width; x++)
            bitmap.SetPixel(x, y, x < Width / 2 ? SKColors.Red : SKColors.Blue);

        using var data = bitmap.Encode(SKEncodedImageFormat.Jpeg, 100);
        return data.ToArray();
    }

    private static byte[] BuildExifPayload(List<Entry> ifd0, List<Entry> subIfd)
    {
        const uint ifd0Offset = 8;                            // straight after the TIFF header
        var ifd0Size = (uint)(2 + (ifd0.Count + 1) * 12 + 4); // +1 for the ExifOffset entry
        var subIfdOffset = ifd0Offset + ifd0Size;
        var subIfdSize = (uint)(2 + subIfd.Count * 12 + 4);

        ifd0.Add(Entry.Long(0x8769, subIfdOffset));           // ExifOffset

        // Sort before assigning offsets, so that the order entries are emitted in and the order
        // their out-of-line data is emitted in cannot disagree. The spec wants tags ascending;
        // callers should not have to add them that way to get a valid file.
        ifd0.Sort((a, b) => a.Tag.CompareTo(b.Tag));
        subIfd.Sort((a, b) => a.Tag.CompareTo(b.Tag));

        // Values too large for the 4-byte inline field live past both IFDs, in emission order.
        var dataOffset = subIfdOffset + subIfdSize;
        foreach (var entry in ifd0.Concat(subIfd).Where(e => e.Data is not null))
        {
            entry.DataOffset = dataOffset;
            dataOffset += (uint)entry.Data!.Length;
        }

        using var tiff = new MemoryStream();
        WriteBytes(tiff, [0x4D, 0x4D, 0x00, 0x2A]); // "MM", magic 42
        WriteUInt32(tiff, ifd0Offset);
        WriteIfd(tiff, ifd0);
        WriteIfd(tiff, subIfd);

        foreach (var entry in ifd0.Concat(subIfd).Where(e => e.Data is not null))
            WriteBytes(tiff, entry.Data!);

        return tiff.ToArray();
    }

    /// <summary>
    /// The bare TIFF block, for containers that embed EXIF without the JPEG "Exif\0\0" wrapper -
    /// JPEG XL's "Exif" box and JPEG 2000's uuid box.
    /// </summary>
    public static byte[] BuildTiff(ushort orientation = 1, string? dateTimeOriginal = "2024:09:26 18:32:17", ushort? compression = null)
    {
        var ifd0 = new List<Entry> { Entry.Short(0x0112, orientation) };
        if (compression is { } value)
            ifd0.Add(Entry.Short(0x0103, value));

        var subIfd = new List<Entry>();
        if (dateTimeOriginal is not null)
            subIfd.Add(Entry.Ascii(0x9003, dateTimeOriginal));
        
        subIfd.Add(Entry.Short(0xA001, 1)); // sRGB

        return BuildExifPayload(ifd0, subIfd);
    }

    /// <summary>
    /// Writes a bare TIFF file - the same block, standing on its own, which is all a TIFF is.
    /// The caller deletes it.
    /// </summary>
    public static string WriteTiff(ushort orientation = 1, ushort? compression = null)
    {
        var path = Path.Combine(Path.GetTempPath(), $"lyra-exif-{Guid.NewGuid():N}.tif");
        File.WriteAllBytes(path, BuildTiff(orientation, dateTimeOriginal: null, compression));
        return path;
    }

    /// <summary>
    /// Splices marker segments in directly after SOI. Each entry is a marker byte (the second
    /// half of 0xFF&lt;marker&gt;) and its payload; the two-byte length field is added here and
    /// counts itself, as JPEG requires.
    /// </summary>
    private static byte[] InsertSegments(byte[] jpeg, List<(byte Marker, byte[] Payload)> segments)
    {
        using var stream = new MemoryStream();
        stream.Write(jpeg, 0, 2); // SOI

        foreach (var (marker, payload) in segments)
        {
            var length = payload.Length + 2;
            WriteBytes(stream, [0xFF, marker, (byte)(length >> 8), (byte)length]);
            WriteBytes(stream, payload);
        }

        stream.Write(jpeg, 2, jpeg.Length - 2);
        return stream.ToArray();
    }

    /// <summary>An XMP packet in the APP1 segment Adobe defined for it.</summary>
    private static byte[] BuildXmpSegment(XmpFields fields) =>
        [.. Encoding.UTF8.GetBytes("http://ns.adobe.com/xap/1.0/\0"), .. BuildXmpPacket(fields)];

    /// <summary>The bare XMP packet, as JPEG XL's "xml " box and JP2's uuid box store it.</summary>
    public static byte[] BuildXmpPacket(XmpFields fields)
    {
        var body = new StringBuilder();
        if (fields.Title is not null)
            body.Append($"<dc:title><rdf:Alt><rdf:li xml:lang=\"x-default\">{Escape(fields.Title)}</rdf:li></rdf:Alt></dc:title>");
        
        if (fields.Description is not null)
            body.Append($"<dc:description><rdf:Alt><rdf:li xml:lang=\"x-default\">{Escape(fields.Description)}</rdf:li></rdf:Alt></dc:description>");
        
        if (fields.Keywords is not null)
            body.Append("<dc:subject><rdf:Bag>"
                        + string.Concat(fields.Keywords.Select(k => $"<rdf:li>{Escape(k)}</rdf:li>"))
                        + "</rdf:Bag></dc:subject>");
        
        if (fields.Creator is not null)
            body.Append($"<dc:creator><rdf:Seq><rdf:li>{Escape(fields.Creator)}</rdf:li></rdf:Seq></dc:creator>");
        
        if (fields.Rights is not null)
            body.Append($"<dc:rights><rdf:Alt><rdf:li xml:lang=\"x-default\">{Escape(fields.Rights)}</rdf:li></rdf:Alt></dc:rights>");

        var rating = fields.Rating is { } value ? $" xmp:Rating=\"{value}\"" : string.Empty;

        var packet =
            "<?xpacket begin=\"﻿\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>"
            + "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\">"
            + "<rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">"
            + "<rdf:Description rdf:about=\"\""
            + " xmlns:dc=\"http://purl.org/dc/elements/1.1/\""
            + " xmlns:xmp=\"http://ns.adobe.com/xap/1.0/\""
            + rating + ">"
            + body
            + "</rdf:Description></rdf:RDF></x:xmpmeta>"
            + "<?xpacket end=\"w\"?>";

        return Encoding.UTF8.GetBytes(packet);
    }

    /// <summary>
    /// IPTC IIM datasets wrapped in a Photoshop image resource block, in APP13 - the layout
    /// every writer has used since Photoshop 3.0.
    /// </summary>
    private static byte[] BuildIptcSegment(IptcFields fields)
    {
        using var datasets = new MemoryStream();
        WriteDataset(datasets, 5, fields.ObjectName);        // 2:05  Object Name
        
        foreach (var keyword in fields.Keywords ?? [])
            WriteDataset(datasets, 25, keyword);             // 2:25  Keywords (repeatable)
        
        WriteDataset(datasets, 80, fields.ByLine);           // 2:80  By-line
        WriteDataset(datasets, 116, fields.CopyrightNotice); // 2:116 Copyright Notice
        WriteDataset(datasets, 120, fields.Caption);         // 2:120 Caption/Abstract

        var iptc = datasets.ToArray();

        using var resource = new MemoryStream();
        WriteBytes(resource, Encoding.ASCII.GetBytes("8BIM"));
        WriteUInt16(resource, 0x0404); // IPTC-NAA resource
        WriteBytes(resource, [0x00, 0x00]); // empty pascal name, padded to even
        WriteUInt32(resource, (uint)iptc.Length);
        WriteBytes(resource, iptc);
        if (iptc.Length % 2 != 0)
            resource.WriteByte(0x00);

        return [.. Encoding.ASCII.GetBytes("Photoshop 3.0\0"), .. resource.ToArray()];

        static void WriteDataset(Stream stream, byte dataset, string? value)
        {
            if (value is null)
                return;

            var bytes = Encoding.UTF8.GetBytes(value);
            WriteBytes(stream, [0x1C, 0x02, dataset, (byte)(bytes.Length >> 8), (byte)bytes.Length]);
            WriteBytes(stream, bytes);
        }
    }

    private static string Escape(string value) =>
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    private static void WriteIfd(Stream stream, List<Entry> entries)
    {
        WriteUInt16(stream, (ushort)entries.Count);

        foreach (var entry in entries) // already sorted by BuildExifPayload
        {
            WriteUInt16(stream, entry.Tag);
            WriteUInt16(stream, entry.Type);
            WriteUInt32(stream, entry.Count);
            WriteUInt32(stream, entry.Data is null ? entry.Inline : entry.DataOffset);
        }

        WriteUInt32(stream, 0); // no next IFD
    }

    private sealed class Entry
    {
        public ushort Tag;
        public ushort Type;
        public uint Count;
        public uint Inline;  // used when Data is null
        public byte[]? Data; // when set, the value field holds DataOffset instead
        public uint DataOffset;

        /// <summary>SHORT values sit in the high half of the 4-byte value field.</summary>
        public static Entry Short(ushort tag, ushort value) =>
            new() { Tag = tag, Type = 3, Count = 1, Inline = (uint)value << 16 };

        public static Entry Long(ushort tag, uint value) =>
            new() { Tag = tag, Type = 4, Count = 1, Inline = value };

        public static Entry Ascii(ushort tag, string value)
        {
            var bytes = Encoding.ASCII.GetBytes(value + '\0');
            return new Entry { Tag = tag, Type = 2, Count = (uint)bytes.Length, Data = bytes };
        }

        public static Entry SignedRational(ushort tag, int numerator, int denominator)
        {
            using var data = new MemoryStream();
            WriteUInt32(data, unchecked((uint)numerator));
            WriteUInt32(data, unchecked((uint)denominator));
            return new Entry { Tag = tag, Type = 10, Count = 1, Data = data.ToArray() };
        }
    }

    private static void WriteBytes(Stream stream, byte[] bytes) => 
        stream.Write(bytes, 0, bytes.Length);

    private static void WriteUInt16(Stream stream, ushort value) =>
        WriteBytes(stream, [(byte)(value >> 8), (byte)value]);

    private static void WriteUInt32(Stream stream, uint value) =>
        WriteBytes(stream, [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value]);
}