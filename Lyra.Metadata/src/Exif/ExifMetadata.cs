using System.Globalization;
using Lyra.Metadata.Tiff;
using Lyra.Metadata.Support;

namespace Lyra.Metadata.Exif;

/// <summary>
/// The EXIF of an image: IFD0 and the EXIF, GPS and Interop directories it points at, with their
/// values read into memory, and the XMP, IPTC and ICC blocks IFD0 carries. Reading needs no more
/// than the directories and those values, however large the file.
/// </summary>
public sealed class ExifMetadata
{
    private const int MaxValueCount = 4096;

    /// <summary>Tags whose values are per strip, tile or sub-image, not facts about the image.</summary>
    private static readonly HashSet<ushort> BulkTags = [273, 279, 288, 289, 320, 324, 325, 330, ExifTags.MakerNote, ExifTags.Xmp, ExifTags.Iptc, ExifTags.Photoshop, ExifTags.IccProfile];

    public static readonly TiffDirectoryKind[] Kinds = [TiffDirectoryKind.Image, TiffDirectoryKind.Exif, TiffDirectoryKind.Gps, TiffDirectoryKind.Interop];

    private readonly Dictionary<TiffDirectoryKind, IReadOnlyDictionary<ushort, ExifValue>> _directories;

    public byte[]? Xmp { get; }
    public byte[]? Iptc { get; }
    public byte[]? Photoshop { get; }
    public byte[]? Icc { get; }

    internal ExifMetadata(IReadOnlyDictionary<TiffDirectoryKind, IReadOnlyDictionary<ushort, ExifValue>> directories, byte[]? xmp = null, byte[]? iptc = null, byte[]? photoshop = null, byte[]? icc = null)
    {
        _directories = new Dictionary<TiffDirectoryKind, IReadOnlyDictionary<ushort, ExifValue>>(directories);
        Xmp = xmp;
        Iptc = iptc;
        Photoshop = photoshop;
        Icc = icc;
    }

    /// <summary>
    /// The EXIF of a TIFF, a BigTIFF, a TIFF-based raw or a bare EXIF block; null when the stream
    /// is none of those. IFD0 is the first directory, the others the first of their kind.
    /// </summary>
    public static ExifMetadata? Read(Stream stream)
    {
        if (TiffDirectoryReader.Read(stream) is not { } layout)
            return null;

        var all = layout.AllDirectories().ToList();
        var ifd0 = layout.Directories[0];

        var directories = new Dictionary<TiffDirectoryKind, IReadOnlyDictionary<ushort, ExifValue>>
        {
            [TiffDirectoryKind.Image] = Values(stream, ifd0, layout.LittleEndian)
        };

        foreach (var kind in Kinds.Skip(1))
            if (all.FirstOrDefault(directory => directory.Kind == kind) is { } directory)
                directories[kind] = Values(stream, directory, layout.LittleEndian);

        return new ExifMetadata(
            directories,
            Block(stream, ifd0, ExifTags.Xmp),
            Block(stream, ifd0, ExifTags.Iptc),
            Block(stream, ifd0, ExifTags.Photoshop),
            Block(stream, ifd0, ExifTags.IccProfile)
        );
    }

    /// <summary>
    /// A TIFF whose first directory is not IFD0 but of the <paramref name="first"/> kind, as CR3
    /// keeps its EXIF and GPS: that directory and those it points at. XMP is taken from an EXIF
    /// directory and ICC from either.
    /// </summary>
    public static ExifMetadata? Read(Stream stream, TiffDirectoryKind first)
    {
        if (first == TiffDirectoryKind.Image)
            return Read(stream);

        if (TiffDirectoryReader.Read(stream, first) is not { Directories: [var root, ..] } layout)
            return null;

        var directories = new Dictionary<TiffDirectoryKind, IReadOnlyDictionary<ushort, ExifValue>>
        {
            [first] = Values(stream, root, layout.LittleEndian)
        };

        foreach (var directory in root.Descendants())
            if (Kinds.Contains(directory.Kind) && !directories.ContainsKey(directory.Kind))
                directories[directory.Kind] = Values(stream, directory, layout.LittleEndian);

        return new ExifMetadata(directories,
            xmp: first == TiffDirectoryKind.Exif ? Block(stream, root, ExifTags.Xmp) : null,
            icc: Block(stream, root, ExifTags.IccProfile));
    }

    /// <summary>
    /// Several EXIF blocks as one, each directory taken from the first block that has it - how a
    /// JPEG with two APP1 segments, or one more in its Photoshop resources, has always been read.
    /// The embedded XMP, IPTC and ICC are not carried over; each block offers its own.
    /// </summary>
    public static ExifMetadata? Combine(IReadOnlyList<ExifMetadata> blocks)
    {
        if (blocks.Count <= 1)
            return blocks.Count == 1 ? blocks[0] : null;

        var directories = new Dictionary<TiffDirectoryKind, IReadOnlyDictionary<ushort, ExifValue>>();
        foreach (var kind in Kinds)
            if (blocks.FirstOrDefault(block => block.Has(kind)) is { } first)
                directories[kind] = first._directories[kind];

        return new ExifMetadata(directories);
    }

    public bool Has(TiffDirectoryKind kind) => _directories.ContainsKey(kind);

    internal IReadOnlyDictionary<ushort, ExifValue> Directory(TiffDirectoryKind kind) => _directories.TryGetValue(kind, out var values) ? values : EmptyValues;

    private static readonly IReadOnlyDictionary<ushort, ExifValue> EmptyValues = new Dictionary<ushort, ExifValue>();

    public ExifValue? Get(TiffDirectoryKind kind, ushort tag) => _directories.TryGetValue(kind, out var values) && values.TryGetValue(tag, out var value) ? value : null;

    public string? Describe(TiffDirectoryKind kind, ushort tag) => ExifDescriber.Describe(this, kind, tag);

    public bool TryGetInt32(TiffDirectoryKind kind, ushort tag, out int value)
    {
        value = 0;
        return Get(kind, tag) is { } exif && exif.TryGetInt32(out value);
    }

    public bool TryGetRational(TiffDirectoryKind kind, ushort tag, out TiffRational value)
    {
        value = default;
        return Get(kind, tag) is { } exif && exif.TryGetRational(out value);
    }

    public bool TryGetDateTime(TiffDirectoryKind kind, ushort tag, out DateTime value)
    {
        value = default;
        return Get(kind, tag)?.Text is { } text && DateTime.TryParseExact(text, DatePatterns, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out value);
    }

    private static readonly string[] DatePatterns =
    [
        "yyyy:MM:dd HH:mm:ss.fff", "yyyy:MM:dd HH:mm:ss.fffzzz", "yyyy:MM:dd HH:mm:ss", "yyyy:MM:dd HH:mm:sszzz",
        "yyyy:MM:dd HH:mm", "yyyy:MM:dd HH:mmzzz",
        "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm:sszzz", "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mmzzz",
        "yyyy.MM.dd HH:mm:ss", "yyyy.MM.dd HH:mm:sszzz", "yyyy.MM.dd HH:mm", "yyyy.MM.dd HH:mmzzz",
        "yyyy-MM-ddTHH:mm:ss.fff", "yyyy-MM-ddTHH:mm:ss.fffzzz", "yyyy-MM-ddTHH:mm:ss.ff", "yyyy-MM-ddTHH:mm:ss.f",
        "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:sszzz", "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mmzzz",
        "yyyy:MM:dd", "yyyy-MM-dd", "yyyy-MM", "yyyyMMdd", "yyyy"
    ];

    private static Dictionary<ushort, ExifValue> Values(Stream stream, TiffDirectory directory, bool little)
    {
        var values = new Dictionary<ushort, ExifValue>();

        foreach (var entry in directory.Entries)
        {
            if (BulkTags.Contains(entry.Tag) || entry.ValueSize > Limits.ValueBytes || values.ContainsKey(entry.Tag))
                continue;

            if (ExifValue.Read(stream, entry, little, MaxValueCount, Limits.ValueBytes) is { } value)
                values[entry.Tag] = value;
        }

        return values;
    }
    
    internal ExifMetadata WithCameraValues(ExifMetadata camera) => new(Underneath(camera._directories, _directories), Xmp, Iptc, Photoshop, Icc);

    /// <summary>The values of <paramref name="primary"/>, with <paramref name="fallback"/> filling only the tags it lacks.</summary>
    private static Dictionary<TiffDirectoryKind, IReadOnlyDictionary<ushort, ExifValue>> Underneath(IReadOnlyDictionary<TiffDirectoryKind, IReadOnlyDictionary<ushort, ExifValue>> primary, IReadOnlyDictionary<TiffDirectoryKind, IReadOnlyDictionary<ushort, ExifValue>> fallback)
    {
        var merged = new Dictionary<TiffDirectoryKind, IReadOnlyDictionary<ushort, ExifValue>>();

        foreach (var kind in Kinds)
        {
            var values = new Dictionary<ushort, ExifValue>();

            if (fallback.TryGetValue(kind, out var under))
                foreach (var (tag, value) in under)
                    values[tag] = value;

            if (primary.TryGetValue(kind, out var over))
                foreach (var (tag, value) in over)
                    values[tag] = value;

            if (values.Count > 0)
                merged[kind] = values;
        }

        return merged;
    }

    private static byte[]? Block(Stream stream, TiffDirectory directory, ushort tag) => directory.Find(tag) is { } entry ? TiffDirectoryReader.ReadBytes(stream, entry, Limits.TiffBlockBytes) : null;
}