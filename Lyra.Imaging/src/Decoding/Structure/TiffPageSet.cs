using Lyra.Imaging.Content;
using Lyra.Imaging.Interop;

namespace Lyra.Imaging.Decoding.Structure;

internal static class TiffPageSet
{
    // SUBFILETYPE bits, from the TIFF 6.0 specification.
    private const uint FileTypeReducedImage = 0x1;
    private const uint FileTypePage = 0x2;
    private const uint FileTypeMask = 0x4;

    /// <summary>
    /// The directory indices that are pages of a document, in file order.
    /// </summary>
    public static List<int> Pages(IReadOnlyList<TiffNative.DirectoryInfo> directories)
    {
        var marked = new List<int>();
        var unmarked = new List<int>();

        for (var i = 0; i < directories.Count; i++)
        {
            var type = directories[i].SubfileType;

            if ((type & FileTypePage) != 0)
                marked.Add(i);
            else if ((type & (FileTypeReducedImage | FileTypeMask)) == 0)
                unmarked.Add(i);
        }

        return marked.Count > 0 ? marked : unmarked;
    }
    
    public static List<ImageVariant> Describe(IReadOnlyList<TiffNative.DirectoryInfo> directories, List<int> pages)
    {
        var variants = new List<ImageVariant>(pages.Count);

        for (var position = 0; position < pages.Count; position++)
        {
            var info = directories[pages[position]];

            // PAGENUMBER when the file carries it, otherwise the position in the chain. Files that
            // set it can disagree with file order, and the tag is what the document itself says.
            var number = info.PageTotal > 0 ? info.PageNumber + 1 : position + 1;

            var detail = $"{DescribeCompression(info.Compression)}, {info.BitsPerSample}-bit";
            if (info.SamplesPerPixel > 1)
                detail += $" x{info.SamplesPerPixel}";

            variants.Add(new ImageVariant(
                Label: $"Page {number}",
                Width: (int)info.Width,
                Height: (int)info.Height,
                Detail: detail,
                ByteSize: (long)info.Width * info.Height * 4)
            );
        }

        return variants;
    }

    public static StructureGroup Summarise(IReadOnlyList<TiffNative.DirectoryInfo> directories, List<int> pages)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("Directories", directories.Count.ToString()),
            new("Pages", pages.Count.ToString())
        };
        
        var levels = directories.Count - pages.Count;
        if (levels > 0)
            fields.Add(new KeyValuePair<string, string>("Reduced-resolution levels", levels.ToString()));

        var compressions = pages
            .Select(i => DescribeCompression(directories[i].Compression))
            .Distinct()
            .ToList();

        fields.Add(new KeyValuePair<string, string>("Compression", string.Join(", ", compressions)));
        
        var sizes = pages.Select(i => $"{directories[i].Width}x{directories[i].Height}").Distinct().Take(4).ToList();
        var shown = sizes.Take(3);

        fields.Add(new KeyValuePair<string, string>("Page size", string.Join(", ", shown) + (sizes.Count > 3 ? ", ..." : "")));

        return new StructureGroup
        {
            Name = "Document",
            Description = pages.Count > 1 ? $"{pages.Count} pages" : "single page",
            Fields = fields
        };
    }
    
    public static void Describe(Composite composite, IReadOnlyList<TiffNative.DirectoryInfo> directories,
        List<int> pages, bool bigTiff)
    {
        if (directories.Count == 0)
            return;

        var first = directories[pages.Count > 0 ? pages[0] : 0];

        composite.AddFormatSpecific("Format", bigTiff ? "BigTIFF (64-bit offsets)" : "TIFF");
        composite.AddFormatSpecific("Compression", DescribeCompression(first.Compression));
        composite.AddFormatSpecific("Photometric", DescribePhotometric(first.Photometric));

        composite.AddFormatSpecific("Sample Depth", first.SamplesPerPixel > 1
            ? $"{first.BitsPerSample}-bit x{first.SamplesPerPixel} ({DescribeSampleFormat(first.SampleFormat)})"
            : $"{first.BitsPerSample}-bit ({DescribeSampleFormat(first.SampleFormat)})");

        composite.AddFormatSpecific("Layout", first.IsTiled != 0
            ? $"tiled {first.TileWidth}x{first.TileHeight}"
            : $"stripped, {first.RowsPerStrip} row{(first.RowsPerStrip == 1 ? "" : "s")} per strip");

        if (first.PlanarConfig == 2)
            composite.AddFormatSpecific("Planar", "separate planes");

        if (pages.Count > 1)
            composite.AddFormatSpecific("Pages", pages.Count.ToString());

        var levels = directories.Count - pages.Count;
        if (levels > 0)
            composite.AddFormatSpecific("Reduced Levels", levels.ToString());

        var compressions = directories.Select(d => DescribeCompression(d.Compression)).Distinct().ToList();
        if (compressions.Count > 1)
            composite.AddFormatSpecific("Mixed Compression", string.Join(", ", compressions));
    }

    private static string DescribePhotometric(ushort photometric) => photometric switch
    {
        0 => "min-is-white (0 is white)",
        1 => "min-is-black",
        2 => "RGB",
        3 => "palette",
        4 => "transparency mask",
        5 => "CMYK",
        6 => "YCbCr",
        8 => "CIE L*a*b*",
        _ => $"photometric {photometric}"
    };

    private static string DescribeSampleFormat(ushort format) => format switch
    {
        1 => "unsigned",
        2 => "signed",
        3 => "float",
        _ => $"format {format}"
    };

    private static string DescribeCompression(ushort compression) => compression switch
    {
        1 => "uncompressed",
        2 => "CCITT RLE",
        3 => "CCITT Group 3",
        4 => "CCITT Group 4",
        5 => "LZW",
        6 or 7 => "JPEG",
        34925 => "LZMA",
        50000 => "Zstd",
        50001 => "WebP",
        34887 => "LERC",
        8 or 32946 => "Deflate",
        32773 => "PackBits",
        34712 => "JPEG 2000",
        _ => $"compression {compression}"
    };
}