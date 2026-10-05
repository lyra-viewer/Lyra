using System.Buffers.Binary;
using System.Text;
using Lyra.ManagedCodecs.Raster.Heif;
using Xunit;

namespace Lyra.ManagedCodecs.Tests.Heif;

public class IsoTrackReaderTests
{
    [Fact]
    public void AnAvifSequence_HasAColorTrackAndItsAlpha()
    {
        var movie = IsoTrackReader.Read(Convert.FromBase64String(HeifSequenceFixtures.Avif));

        Assert.NotNull(movie);
        var color = Assert.IsType<IsoTrack>(movie.ColorTrack);

        Assert.Equal((1u, "pict", "av01", 100u), (color.Id, color.Handler, color.SampleEntry, color.Timescale));
        Assert.Equal([10u, 20u, 5u], color.SampleDurations);
        Assert.Equal(3, color.SampleSizes.Count);
        Assert.True(color.PlaysSamplesInOrder);

        var alpha = Assert.IsType<IsoTrack>(movie.AlphaTrackFor(color));
        Assert.Equal((2u, "auxv", 1u), (alpha.Id, alpha.Handler, alpha.AuxiliaryFor));
        Assert.Contains("alpha", alpha.AuxiliaryType);
    }

    [Fact]
    public void AnAlphaTrackUnderAPictHandler_IsStillAlpha()
    {
        var movie = IsoTrackReader.Read(Convert.FromBase64String(HeifSequenceFixtures.AvifOldAlpha))!;

        Assert.Equal(1u, movie.ColorTrack!.Id);
        Assert.Equal(2u, movie.AlphaTrackFor(movie.ColorTrack)!.Id);
    }

    [Fact]
    public void AnHevcSequence_IsRead()
    {
        var movie = IsoTrackReader.Read(Convert.FromBase64String(HeifSequenceFixtures.Hevc))!;

        Assert.Equal(("hvc1", 3), (movie.ColorTrack!.SampleEntry, movie.ColorTrack.SampleCount));
        Assert.Equal([10u, 10u, 10u], movie.ColorTrack.SampleDurations);
    }

    [Fact]
    public void TheMovieIsFound_WithoutReadingTheMediaBox()
    {
        var mdat = Box("mdat", new byte[300_000]);
        byte[] file = [.. Box("ftyp", "avis"u8.ToArray()), .. mdat, .. Movie(Track(1, "pict", stts: [(3, 10)]))];

        var stream = new CountingStream(file);

        Assert.Equal(3, IsoTrackReader.ReadMovie(stream)!.ColorTrack!.SampleCount);
        Assert.True(stream.BytesRead < 2_000, $"read {stream.BytesRead} bytes of {file.Length}");
    }

    [Fact]
    public void AMovieBoxTooLargeToBelieve_IsNotRead()
    {
        var header = new byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)IsoTrackReader.MaxMovieBytes + 8);
        "moov"u8.CopyTo(header.AsSpan(4));

        Assert.Null(IsoTrackReader.ReadMovie(new MemoryStream([.. Box("ftyp", "avis"u8.ToArray()), .. header])));
    }

    [Fact]
    public void AStillImage_HasNoMovie()
    {
        byte[] file = [.. Box("ftyp", "avif"u8.ToArray()), .. Box("meta", new byte[40]), .. Box("mdat", new byte[64])];

        Assert.Null(IsoTrackReader.ReadMovie(new MemoryStream(file)));
        Assert.Null(IsoTrackReader.Read(file));
    }

    [Fact]
    public void DurationRuns_AreExpandedPerSample()
    {
        var movie = IsoTrackReader.Read(Movie(Track(1, "pict", stts: [(2, 7), (1, 30), (3, 1)])))!;

        Assert.Equal([7u, 7u, 30u, 1u, 1u, 1u], movie.ColorTrack!.SampleDurations);
    }

    [Fact]
    public void AnAbsurdSampleCount_IsNotBelieved()
    {
        var movie = IsoTrackReader.Read(Movie(Track(1, "pict", stts: [(uint.MaxValue, 1)])))!;

        Assert.Empty(movie.Tracks);
    }

    [Fact]
    public void ACountLargerThanItsTable_IsNotBelieved()
    {
        // An stts claiming two entries but holding one.
        var stts = FullBox("stts", [.. U32(2), .. U32(3), .. U32(10)]);
        var movie = IsoTrackReader.Read(Movie(Track(1, "pict", sttsBox: stts)))!;

        Assert.Empty(movie.Tracks);
    }

    [Fact]
    public void AVersion1HeaderTooShortForItsTimescale_DropsTheTrack()
    {
        // Version 1 moves the timescale from offset 12 to 20; this mdhd ends before it.
        var mdhd = Box("mdhd", [1, 0, 0, 0, .. new byte[16]]);
        var movie = IsoTrackReader.Read(Movie(Track(1, "pict", stts: [(3, 1)], mdhdBox: mdhd)))!;

        Assert.Empty(movie.Tracks);
    }

    [Fact]
    public void AnAuxiliaryTrackNamingAnotherType_IsNotAlpha()
    {
        var movie = IsoTrackReader.Read(Movie(
            Track(1, "pict", stts: [(2, 1)]),
            Track(2, "auxv", stts: [(2, 1)], auxiliaryFor: 1, auxiliaryUrn: "urn:mpeg:hevc:2015:auxid:2")))!;

        Assert.Null(movie.AlphaTrackFor(movie.ColorTrack!));
    }

    [Theory]
    [InlineData(new long[0], true)]
    [InlineData(new long[] { 0 }, true)]
    [InlineData(new long[] { 5 }, false)]
    [InlineData(new long[] { -1, 0 }, false)]
    [InlineData(new long[] { 0, 0 }, false)]
    public void OnlyAnEditListPlayingFromTheStart_KeepsTheSampleOrder(long[] mediaTimes, bool inOrder)
    {
        var movie = IsoTrackReader.Read(Movie(Track(1, "pict", stts: [(3, 1)], edits: mediaTimes)))!;

        Assert.Equal(inOrder, movie.ColorTrack!.PlaysSamplesInOrder);
    }

    [Fact]
    public void ACutMovie_IsReadAsFarAsItGoes()
    {
        var file = Movie(Track(1, "pict", stts: [(3, 1)]), Track(2, "pict", stts: [(4, 1)]));

        // The moov's own size now runs past the data, so it cannot be found at all.
        Assert.Null(IsoTrackReader.Read(file[..^10]));
    }

    private sealed class CountingStream(byte[] data) : MemoryStream(data, writable: false)
    {
        public long BytesRead { get; private set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = base.Read(buffer, offset, count);
            BytesRead += read;
            return read;
        }

        public override int ReadByte()
        {
            var value = base.ReadByte();
            if (value >= 0)
                BytesRead++;

            return value;
        }
    }

    private static byte[] Movie(params byte[][] tracks) =>
        Box("moov", [.. FullBox("mvhd", [.. U32(0), .. U32(0), .. U32(100), .. U32(0), .. new byte[80]]), .. tracks.SelectMany(t => t)]);

    private static byte[] Track(
        uint id,
        string handler,
        (uint Count, uint Delta)[]? stts = null,
        byte[]? sttsBox = null,
        uint? auxiliaryFor = null,
        string? auxiliaryUrn = null,
        long[]? edits = null,
        byte[]? mdhdBox = null)
    {
        var tkhd = FullBox("tkhd", [.. U32(0), .. U32(0), .. U32(id), .. new byte[68]]);
        var mdhd = mdhdBox ?? FullBox("mdhd", [.. U32(0), .. U32(0), .. U32(100), .. U32(0), .. new byte[4]]);
        var hdlr = FullBox("hdlr", [.. U32(0), .. Encoding.ASCII.GetBytes(handler), .. new byte[13]]);

        sttsBox ??= FullBox("stts", [.. U32((uint)stts!.Length), .. stts.SelectMany(e => (byte[])[.. U32(e.Count), .. U32(e.Delta)])]);

        var auxi = auxiliaryUrn is null ? [] : FullBox("auxi", [.. Encoding.ASCII.GetBytes(auxiliaryUrn), 0]);
        var entry = Box("av01", [.. new byte[78], .. auxi]);
        var stsd = FullBox("stsd", [.. U32(1), .. entry]);

        var stbl = Box("stbl", [.. stsd, .. sttsBox]);
        var mdia = Box("mdia", [.. mdhd, .. hdlr, .. Box("minf", stbl)]);

        var tref = auxiliaryFor is { } of ? Box("tref", Box("auxl", U32(of))) : [];
        var edts = edits is null ? [] : Box("edts", FullBox("elst", [.. U32((uint)edits.Length), .. edits.SelectMany(t => (byte[])[.. U32(10), .. U32((uint)(int)t), 0, 1, 0, 0])]));

        return Box("trak", [.. tkhd, .. tref, .. edts, .. mdia]);
    }

    private static byte[] Box(string type, byte[] payload)
    {
        var box = new byte[8 + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(box, (uint)box.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(box, 4);
        payload.CopyTo(box, 8);
        return box;
    }

    private static byte[] FullBox(string type, byte[] payload) => Box(type, [0, 0, 0, 0, .. payload]);

    private static byte[] U32(uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        return bytes;
    }
}