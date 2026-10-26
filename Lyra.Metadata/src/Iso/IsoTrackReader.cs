using System.Buffers.Binary;
using System.Text;
using Lyra.Metadata.Support;

namespace Lyra.Metadata.Iso;

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
    
    public bool IsAlpha => AuxiliaryFor is not null && (AuxiliaryType is null || AuxiliaryType.Contains("alpha", StringComparison.OrdinalIgnoreCase));
    
    public bool PlaysSamplesInOrder => EditMediaTimes is [] or [0];
}

public sealed record IsoMovie(IReadOnlyList<IsoTrack> Tracks)
{
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
    private const int MaxTopLevelBoxes = 1024;

    /// <summary>
    /// Finds the top-level moov box and reads its tracks, reading nothing else: box bodies are
    /// seeked over, so a large mdat costs no more than its header. Null when there is no movie.
    /// </summary>
    public static IsoMovie? ReadMovie(Stream stream)
    {
        using var _ = stream.KeepPosition();
        var offset = 0L;

        for (var i = 0; i < MaxTopLevelBoxes; i++)
        {
            if (IsoBoxes.ReadHeader(stream, offset, stream.Length) is not { } box)
                return null;

            if (box.Type == "moov")
            {
                return stream.ReadBytesAt(box.Offset, box.Size, Limits.MovieBytes) is { } moov ? Read(moov) : null;
            }

            offset = box.End;
        }

        return null;
    }

    public static IsoMovie? Read(ReadOnlySpan<byte> data)
    {
        if (!IsoBoxes.TryFind(data, out var moov, "moov"))
            return null;

        var tracks = new List<IsoTrack>();

        foreach (var box in IsoBoxes.Children(moov))
        {
            if (box.Is("trak"u8) && ReadTrack(box.Body) is { } track)
                tracks.Add(track);
        }

        return new IsoMovie(tracks);
    }

    private static IsoTrack? ReadTrack(ReadOnlySpan<byte> trak)
    {
        if (!IsoBoxes.TryFind(trak, out var tkhd, "tkhd")
            || IsoSampleTables.TrackHeader(tkhd) is not { } header
            || !IsoBoxes.TryFind(trak, out var mdhd, "mdia", "mdhd") || IsoSampleTables.Versioned(mdhd, 12, 20) is not { } timescale
            || !IsoBoxes.TryFind(trak, out var hdlr, "mdia", "hdlr") || hdlr.Length < 12
            || !IsoBoxes.TryFind(trak, out var stbl, "mdia", "minf", "stbl")
            || !IsoBoxes.TryFind(stbl, out var stts, "stts") || IsoSampleTables.SampleDurations(stts) is not { } durations)
            return null;

        var handler = Encoding.ASCII.GetString(hdlr.Slice(8, 4));
        var sizes = IsoBoxes.TryFind(stbl, out var stsz, "stsz") ? IsoSampleTables.SampleSizes(stsz, durations.Length) ?? [] : [];
        var entry = IsoBoxes.TryFind(stbl, out var stsd, "stsd") ? IsoSampleTables.SampleEntry(stsd) : null;

        uint? auxiliaryFor = null;
        if (IsoBoxes.TryFind(trak, out var auxl, "tref", "auxl") && auxl.Length >= 4)
            auxiliaryFor = BinaryPrimitives.ReadUInt32BigEndian(auxl);

        var edits = IsoBoxes.TryFind(trak, out var elst, "edts", "elst") ? IsoSampleTables.EditMediaTimes(elst) : [];

        return new IsoTrack(header.Id, handler, entry?.Type ?? "", timescale, durations, sizes, auxiliaryFor, entry?.AuxiliaryType, edits);
    }
}