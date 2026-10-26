using System.Globalization;
using Lyra.Imaging.Content;
using Lyra.Metadata.Iptc;
using Lyra.Metadata.Xmp;
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

    public static void Apply(XmpPacket? xmp, IptcRecords? iptc, ExifInfo exifInfo)
    {
        exifInfo.Title = Truncate(FirstNonEmpty(
            First(xmp, XmpNamespaces.Dc, "title"),
            Iptc(iptc, IptcRecords.ObjectName),
            Iptc(iptc, IptcRecords.Headline)
        ), MaxTitleLength);

        exifInfo.Description = Truncate(FirstNonEmpty(
            First(xmp, XmpNamespaces.Dc, "description"),
            Iptc(iptc, IptcRecords.Caption)
        ), MaxDescriptionLength);

        exifInfo.Keywords = ReadKeywords(xmp, iptc);
        exifInfo.Rating = DescribeRating(First(xmp, XmpNamespaces.Xmp, "Rating"));

        exifInfo.Artist = FirstNonEmpty(
            exifInfo.Artist,
            First(xmp, XmpNamespaces.Dc, "creator"),
            Iptc(iptc, IptcRecords.ByLine)
        );

        exifInfo.Copyright = FirstNonEmpty(
            exifInfo.Copyright,
            First(xmp, XmpNamespaces.Dc, "rights"),
            Iptc(iptc, IptcRecords.CopyrightNotice)
        );
    }

    /// <summary>Every non-blank value of an XMP property, trimmed.</summary>
    private static List<string> All(XmpPacket? xmp, string ns, string name) =>
        xmp?.Values(ns, name).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).ToList() ?? [];

    private static string First(XmpPacket? xmp, string ns, string name) => All(xmp, ns, name) is [var first, ..] ? first : string.Empty;

    /// <summary>A dataset as one line; repeats are joined with a space, as the panel has always shown them.</summary>
    private static string Iptc(IptcRecords? iptc, byte dataSet) => iptc is null ? string.Empty : Clean(string.Join(' ', iptc.Get(IptcRecords.ApplicationRecord, dataSet)));

    private static string ReadKeywords(XmpPacket? xmp, IptcRecords? iptc)
    {
        var keywords = All(xmp, XmpNamespaces.Dc, "subject");
        if (keywords.Count == 0 && iptc is not null)
            keywords = Clean(string.Join(';', iptc.Get(IptcRecords.ApplicationRecord, IptcRecords.Keywords)))
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
