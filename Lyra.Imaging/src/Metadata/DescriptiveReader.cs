using System.Globalization;
using Lyra.Imaging.Content;
using MetadataExtractor.Formats.Iptc;
using MetadataExtractor.Formats.Xmp;
using Directory = MetadataExtractor.Directory;
using static Lyra.Imaging.Metadata.MetadataValues;

namespace Lyra.Imaging.Metadata;

/// <summary>
/// Title, description, keywords, rating and authorship - the parts a person writes rather than
/// the camera, and the only ones that can live in three places at once.
///
/// XMP is preferred over IPTC where both are present, because writers keep XMP current and leave
/// the IPTC block behind as a legacy copy. EXIF wins over both for authorship, being the more
/// specific source. Runs after <see cref="ExifReader"/>, whose values it defers to.
/// </summary>
internal static class DescriptiveReader
{
    // Free-text metadata is unbounded in the file but bounded in the panel, whose rows are
    // single-line and do not wrap. These caps keep a stock photo's caption on its own row.
    private const int MaxTitleLength = 120;
    private const int MaxDescriptionLength = 160;
    private const int MaxKeywords = 8;

    public static void Apply(IReadOnlyList<Directory> directories, ExifInfo exifInfo)
    {
        var xmp = directories.OfType<XmpDirectory>().FirstOrDefault()?.GetXmpProperties() ?? new Dictionary<string, string>();
        var iptc = directories.OfType<IptcDirectory>().FirstOrDefault();

        exifInfo.Title = Truncate(FirstNonEmpty(
            XmpProperties.First(xmp, "dc:title"),
            Describe(iptc, IptcDirectory.TagObjectName),
            Describe(iptc, IptcDirectory.TagHeadline)
        ), MaxTitleLength);

        exifInfo.Description = Truncate(FirstNonEmpty(
            XmpProperties.First(xmp, "dc:description"),
            Describe(iptc, IptcDirectory.TagCaption)
        ), MaxDescriptionLength);

        exifInfo.Keywords = ReadKeywords(xmp, iptc);
        exifInfo.Rating = DescribeRating(XmpProperties.First(xmp, "xmp:Rating"));

        exifInfo.Artist = FirstNonEmpty(
            exifInfo.Artist,
            XmpProperties.First(xmp, "dc:creator"),
            Describe(iptc, IptcDirectory.TagByLine)
        );

        exifInfo.Copyright = FirstNonEmpty(
            exifInfo.Copyright,
            XmpProperties.First(xmp, "dc:rights"),
            Describe(iptc, IptcDirectory.TagCopyrightNotice)
        );
    }

    private static string ReadKeywords(IDictionary<string, string> xmp, Directory? iptc)
    {
        // IPTC returns its repeated field already joined, with its own separator.
        var keywords = XmpProperties.All(xmp, "dc:subject");
        if (keywords.Count == 0)
            keywords = Describe(iptc, IptcDirectory.TagKeywords)
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

        if (keywords.Count == 0)
            return string.Empty;

        // Stock photography routinely tags fifty keywords; the rest are summarized rather than
        // pushed off the edge of the row.
        if (keywords.Count <= MaxKeywords)
            return string.Join(", ", keywords);

        return string.Join(", ", keywords.Take(MaxKeywords)) + $" (+{keywords.Count - MaxKeywords} more)";
    }
    
    private static string DescribeRating(string value)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rating))
            return string.Empty;

        return rating switch
        {
            < 0 => "Rejected",
            0 => string.Empty,
            <= 5 => $"{rating} / 5",
            _ => string.Empty
        };
    }
}
