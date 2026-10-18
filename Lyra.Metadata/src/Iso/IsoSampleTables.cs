using System.Buffers.Binary;
using System.Text;
using Lyra.Metadata.Support;

namespace Lyra.Metadata.Iso;

public readonly record struct IsoTrackHeader(uint Id, long Created);

public readonly record struct IsoSampleEntry(string Type, int Width, int Height, string? AuxiliaryType);

/// <summary>
/// The tables of a track's boxes, each read from the box's body - version and flags included.
/// Every count is checked against the bytes it claims, so a damaged table reads as none.
/// </summary>
public static class IsoSampleTables
{
    public const int MaxSamples = 1_000_000;

    /// <summary>tkhd: version 1 widens the times to 64 bits and pushes the id along.</summary>
    public static IsoTrackHeader? TrackHeader(ReadOnlySpan<byte> tkhd)
    {
        var wide = tkhd is [1, ..];
        if (tkhd.Length < (wide ? 24 : 16))
            return null;

        var created = wide ? (long)BinaryPrimitives.ReadUInt64BigEndian(tkhd[4..]) : BinaryPrimitives.ReadUInt32BigEndian(tkhd[4..]);
        return new IsoTrackHeader(BinaryPrimitives.ReadUInt32BigEndian(tkhd[(wide ? 20 : 12)..]), created);
    }

    /// <summary>A full box's 32-bit field, which version 1's 64-bit times push further along: mdhd's timescale.</summary>
    public static uint? Versioned(ReadOnlySpan<byte> box, int version0Offset, int version1Offset)
    {
        var offset = box is [1, ..] ? version1Offset : version0Offset;
        return box.Length < offset + 4 ? null : BinaryPrimitives.ReadUInt32BigEndian(box[offset..]);
    }

    /// <summary>stsd's first entry, a box of its own after the full box header and entry count.</summary>
    public static IsoSampleEntry? SampleEntry(ReadOnlySpan<byte> stsd)
    {
        if (stsd.Length < 16)
            return null;

        var entry = stsd[8..];
        var size = BinaryPrimitives.ReadUInt32BigEndian(entry);
        if (size < 8 || size > entry.Length)
            return null;

        var type = Encoding.ASCII.GetString(entry.Slice(4, 4));
        var width = size >= 36 ? BinaryPrimitives.ReadUInt16BigEndian(entry[32..]) : 0;
        var height = size >= 36 ? BinaryPrimitives.ReadUInt16BigEndian(entry[34..]) : 0;

        // A visual sample entry's own fields run 78 bytes past its header; its child boxes follow.
        const int visualEntryFields = 8 + 78;
        string? auxiliary = null;
        if (size > visualEntryFields && IsoBoxes.TryFind(entry[visualEntryFields..(int)size], out var auxi, "auxi") && auxi.Length > 4) 
            auxiliary = Encoding.ASCII.GetString(ByteText.UntilNul(auxi[4..]));

        return new IsoSampleEntry(type, width, height, auxiliary);
    }

    /// <summary>stts: runs of (count, delta), expanded to one duration per sample.</summary>
    public static uint[]? SampleDurations(ReadOnlySpan<byte> stts)
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

    /// <summary>stsz: one size for every sample, or a size each. Null when it disagrees with the sample count.</summary>
    public static uint[]? SampleSizes(ReadOnlySpan<byte> stsz, int samples)
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

    /// <summary>stsz's first sample size: the one size of them all, or else the first entry.</summary>
    public static uint? FirstSampleSize(ReadOnlySpan<byte> stsz)
    {
        if (stsz.Length < 16)
            return null;

        var uniform = BinaryPrimitives.ReadUInt32BigEndian(stsz[4..]);
        return uniform != 0 ? uniform : BinaryPrimitives.ReadUInt32BigEndian(stsz[12..]);
    }

    /// <summary>Where the first chunk starts: stco's 32-bit offset, or co64's 64-bit one.</summary>
    public static long? FirstChunkOffset(ReadOnlySpan<byte> table, bool large)
    {
        if (table.Length < (large ? 16 : 12))
            return null;

        return large ? BinaryPrimitives.ReadInt64BigEndian(table[8..]) : BinaryPrimitives.ReadUInt32BigEndian(table[8..]);
    }

    /// <summary>elst: where each edit starts in the media, -1 for an empty edit.</summary>
    public static long[] EditMediaTimes(ReadOnlySpan<byte> elst)
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
}