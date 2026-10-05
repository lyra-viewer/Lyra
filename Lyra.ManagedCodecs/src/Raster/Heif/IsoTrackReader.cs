using System.Buffers.Binary;
using System.Text;

namespace Lyra.ManagedCodecs.Raster.Heif;

/// <param name="Handler">The handler type: "pict" for an image sequence, "vide" for video, "auxv" for an auxiliary track.</param>
/// <param name="SampleEntry">The first sample entry's type, such as "av01" or "hvc1".</param>
/// <param name="SampleDurations">Each sample's duration in <paramref name="Timescale"/> ticks, from stts.</param>
/// <param name="SampleSizes">Each sample's encoded size from stsz; empty when the track has none.</param>
/// <param name="AuxiliaryFor">The track this one is auxiliary to, from tref/auxl.</param>
/// <param name="AuxiliaryType">The URN the sample entry's auxi box declares, when it has one.</param>
/// <param name="EditMediaTimes">Where in the media each edit starts, in media ticks (-1 for an empty edit); empty without an edit list.</param>
public sealed record IsoTrack(
    uint Id,
    string Handler,
    string SampleEntry,
    uint Timescale,
    IReadOnlyList<uint> SampleDurations,
    IReadOnlyList<uint> SampleSizes,
    uint? AuxiliaryFor,
    string? AuxiliaryType,
    IReadOnlyList<long> EditMediaTimes
)
{
    public int SampleCount => SampleDurations.Count;

    public bool IsVisual => Handler is "pict" or "vide" or "auxv";

    /// <summary>
    /// An auxiliary track holding alpha: libavif's older files mark one only by its auxl
    /// reference, under a "pict" handler; newer ones add an auxi box naming the alpha URN.
    /// </summary>
    public bool IsAlpha => AuxiliaryFor is not null && (AuxiliaryType is null || AuxiliaryType.Contains("alpha", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// True when playing the track shows its samples once each, in order, from the first: no
    /// edit list, or a single one starting at the media's beginning (repeated or not).
    /// </summary>
    public bool PlaysSamplesInOrder => EditMediaTimes is [] or [0];
}

public sealed record IsoMovie(IReadOnlyList<IsoTrack> Tracks)
{
    /// <summary>The first visual track that is not auxiliary to another: what libheif decodes as the sequence.</summary>
    public IsoTrack? ColorTrack => Tracks.FirstOrDefault(t => t.IsVisual && t.AuxiliaryFor is null);

    public IsoTrack? AlphaTrackFor(IsoTrack color) => Tracks.FirstOrDefault(t => t.AuxiliaryFor == color.Id && t.IsAlpha);
}

/// <summary>
/// Reads the movie box of an ISO base media file - HEIF and AVIF image sequences - for its
/// tracks' sample tables: how many samples, how long each shows and how large each is. Never
/// touches the media data. Every count and size is checked against the data it claims to
/// describe, so a damaged table drops its track rather than causing a huge allocation.
/// </summary>
public static class IsoTrackReader
{
    /// <summary>Above this many samples a track is not believed; no real sequence comes near it.</summary>
    public const int MaxSamples = 1_000_000;

    private const int MaxTopLevelBoxes = 1024;

    /// <summary>Above this a movie box is not believed: a million samples' tables fit in a fraction of it.</summary>
    public const int MaxMovieBytes = 64 * 1024 * 1024;

    /// <summary>
    /// Finds the top-level moov box and reads its tracks, reading nothing else: box bodies are
    /// seeked over, so a large mdat costs no more than its header. Null when there is no movie.
    /// </summary>
    public static IsoMovie? ReadMovie(Stream stream)
    {
        Span<byte> header = stackalloc byte[16];
        stream.Position = 0;

        for (var i = 0; i < MaxTopLevelBoxes; i++)
        {
            var start = stream.Position;
            if (stream.ReadAtLeast(header[..8], 8, throwOnEndOfStream: false) < 8)
                return null;

            long size = BinaryPrimitives.ReadUInt32BigEndian(header);
            if (size == 1)
            {
                if (stream.ReadAtLeast(header[8..16], 8, throwOnEndOfStream: false) < 8)
                    return null;

                size = (long)Math.Min(BinaryPrimitives.ReadUInt64BigEndian(header[8..]), long.MaxValue);
            }
            else if (size == 0)
            {
                size = stream.Length - start;
            }

            if (size < 8 || size > stream.Length - start)
                return null;

            if (header.Slice(4, 4).SequenceEqual("moov"u8))
            {
                if (size > MaxMovieBytes)
                    return null;

                var moov = new byte[size];
                stream.Position = start;
                stream.ReadExactly(moov);

                return Read(moov);
            }

            stream.Position = start + size;
        }

        return null;
    }

    /// <summary>Null when the data has no movie box; tracks whose tables cannot be read are left out.</summary>
    public static IsoMovie? Read(ReadOnlySpan<byte> data)
    {
        if (!TryFind(data, out var moov, "moov"))
            return null;

        var tracks = new List<IsoTrack>();

        foreach (var (type, body) in Children(moov))
        {
            if (type.SequenceEqual("trak"u8) && ReadTrack(body) is { } track)
                tracks.Add(track);
        }

        return new IsoMovie(tracks);
    }

    private static IsoTrack? ReadTrack(ReadOnlySpan<byte> trak)
    {
        if (!TryFind(trak, out var tkhd, "tkhd") || !TryReadVersioned(tkhd, 12, 20, out var id)
            || !TryFind(trak, out var mdhd, "mdia", "mdhd") || !TryReadVersioned(mdhd, 12, 20, out var timescale)
            || !TryFind(trak, out var hdlr, "mdia", "hdlr") || hdlr.Length < 12
            || !TryFind(trak, out var stbl, "mdia", "minf", "stbl")
            || !TryFind(stbl, out var stts, "stts") || ReadDurations(stts) is not { } durations)
            return null;

        var handler = Encoding.ASCII.GetString(hdlr.Slice(8, 4));
        var sizes = TryFind(stbl, out var stsz, "stsz") ? ReadSizes(stsz, durations.Length) ?? [] : [];
        var (sampleEntry, auxiliaryType) = TryFind(stbl, out var stsd, "stsd") ? ReadSampleEntry(stsd) : ("", null);

        uint? auxiliaryFor = null;
        if (TryFind(trak, out var auxl, "tref", "auxl") && auxl.Length >= 4)
            auxiliaryFor = BinaryPrimitives.ReadUInt32BigEndian(auxl);

        var edits = TryFind(trak, out var elst, "edts", "elst") ? ReadEditMediaTimes(elst) : [];

        return new IsoTrack(id, handler, sampleEntry, timescale, durations, sizes, auxiliaryFor, auxiliaryType, edits);
    }

    /// <summary>A full box's 32-bit field, which version 1's 64-bit times push further along.</summary>
    private static bool TryReadVersioned(ReadOnlySpan<byte> box, int version0Offset, int version1Offset, out uint value)
    {
        var offset = box is [1, ..] ? version1Offset : version0Offset;
        if (box.Length < offset + 4)
        {
            value = 0;
            return false;
        }

        value = BinaryPrimitives.ReadUInt32BigEndian(box[offset..]);
        return true;
    }

    /// <summary>stts: runs of (count, delta), expanded to one duration per sample.</summary>
    private static uint[]? ReadDurations(ReadOnlySpan<byte> stts)
    {
        if (stts.Length < 8)
            return null;

        var entries = BinaryPrimitives.ReadUInt32BigEndian(stts[4..]);
        if (entries > (uint)(stts.Length - 8) / 8)
            return null;

        long total = 0;
        for (var i = 0; i < entries; i++)
        {
            total += BinaryPrimitives.ReadUInt32BigEndian(stts[(8 + i * 8)..]);
            if (total > MaxSamples)
                return null;
        }

        var durations = new uint[total];
        var at = 0;

        for (var i = 0; i < entries; i++)
        {
            var count = (int)BinaryPrimitives.ReadUInt32BigEndian(stts[(8 + i * 8)..]);
            var delta = BinaryPrimitives.ReadUInt32BigEndian(stts[(12 + i * 8)..]);

            durations.AsSpan(at, count).Fill(delta);
            at += count;
        }

        return durations;
    }

    /// <summary>stsz: one size for every sample, or a size each. Null when it disagrees with stts.</summary>
    private static uint[]? ReadSizes(ReadOnlySpan<byte> stsz, int samples)
    {
        if (stsz.Length < 12)
            return null;

        var uniform = BinaryPrimitives.ReadUInt32BigEndian(stsz[4..]);
        var count = BinaryPrimitives.ReadUInt32BigEndian(stsz[8..]);

        if (count != samples)
            return null;

        if (uniform != 0)
            return Enumerable.Repeat(uniform, samples).ToArray();

        if (count > (uint)(stsz.Length - 12) / 4)
            return null;

        var sizes = new uint[count];
        for (var i = 0; i < sizes.Length; i++)
            sizes[i] = BinaryPrimitives.ReadUInt32BigEndian(stsz[(12 + i * 4)..]);

        return sizes;
    }

    /// <summary>The first sample entry's type, and the URN of an auxi box inside it.</summary>
    private static (string Type, string? AuxiliaryType) ReadSampleEntry(ReadOnlySpan<byte> stsd)
    {
        // Full box header, entry count, then the first entry as a box of its own.
        if (stsd.Length < 16)
            return ("", null);

        var entry = stsd[8..];
        var size = BinaryPrimitives.ReadUInt32BigEndian(entry);
        if (size < 8 || size > entry.Length)
            return ("", null);

        var type = Encoding.ASCII.GetString(entry.Slice(4, 4));

        // A visual sample entry's own fields run 78 bytes past its header; its child boxes follow.
        const int visualEntryFields = 8 + 78;
        if (size <= visualEntryFields)
            return (type, null);

        string? auxiliary = null;
        if (TryFind(entry[visualEntryFields..(int)size], out var auxi, "auxi") && auxi.Length > 4)
        {
            var urn = auxi[4..];
            var end = urn.IndexOf((byte)0);
            auxiliary = Encoding.ASCII.GetString(end >= 0 ? urn[..end] : urn);
        }

        return (type, auxiliary);
    }

    private static long[] ReadEditMediaTimes(ReadOnlySpan<byte> elst)
    {
        if (elst.Length < 8)
            return [];

        var version = elst[0];
        var entrySize = version == 1 ? 20 : 12;
        var entries = BinaryPrimitives.ReadUInt32BigEndian(elst[4..]);

        if (entries > (uint)(elst.Length - 8) / entrySize)
            return [];

        // Each entry: segment duration, then media time, both 64-bit in version 1.
        var times = new long[entries];
        for (var i = 0; i < times.Length; i++)
        {
            var entry = elst[(8 + i * entrySize)..];
            times[i] = version == 1 ? BinaryPrimitives.ReadInt64BigEndian(entry[8..]) : BinaryPrimitives.ReadInt32BigEndian(entry[4..]);
        }

        return times;
    }

    /// <summary>The box at the end of a path of child box types, each looked up inside the one before.</summary>
    private static bool TryFind(ReadOnlySpan<byte> parent, out ReadOnlySpan<byte> body, params ReadOnlySpan<string> path)
    {
        Span<byte> type = stackalloc byte[4];
        body = parent;

        foreach (var name in path)
        {
            Encoding.ASCII.GetBytes(name, type);
            if (!TryFindChild(body, type, out body))
                return false;
        }

        return true;
    }

    private static bool TryFindChild(ReadOnlySpan<byte> parent, scoped ReadOnlySpan<byte> type, out ReadOnlySpan<byte> body)
    {
        foreach (var (childType, childBody) in Children(parent))
        {
            if (childType.SequenceEqual(type))
            {
                body = childBody;
                return true;
            }
        }

        body = default;
        return false;
    }

    private static BoxEnumerator Children(ReadOnlySpan<byte> parent) => new(parent);

    private readonly ref struct Box(ReadOnlySpan<byte> type, ReadOnlySpan<byte> body)
    {
        public ReadOnlySpan<byte> Type { get; } = type;

        public ReadOnlySpan<byte> Body { get; } = body;

        public void Deconstruct(out ReadOnlySpan<byte> type, out ReadOnlySpan<byte> body)
        {
            type = Type;
            body = Body;
        }
    }

    /// <summary>The boxes directly inside a span, stopping at the first that does not fit.</summary>
    private ref struct BoxEnumerator(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> _data = data;
        private int _pos;

        public Box Current { get; private set; }

        public readonly BoxEnumerator GetEnumerator() => this;

        public bool MoveNext()
        {
            if (_pos + 8 > _data.Length)
                return false;

            var size = (long)BinaryPrimitives.ReadUInt32BigEndian(_data[_pos..]);
            var header = 8;

            if (size == 1)
            {
                if (_pos + 16 > _data.Length)
                    return false;

                var large = BinaryPrimitives.ReadUInt64BigEndian(_data[(_pos + 8)..]);
                size = large > long.MaxValue ? long.MaxValue : (long)large;
                header = 16;
            }
            else if (size == 0)
            {
                size = _data.Length - _pos;
            }

            if (size < header || size > _data.Length - _pos)
                return false;

            Current = new Box(_data.Slice(_pos + 4, 4), _data.Slice(_pos + header, (int)size - header));
            _pos += (int)size;
            return true;
        }
    }
}