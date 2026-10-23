using Lyra.Metadata.Iso;
using Xunit;
using static Lyra.Metadata.Tests.Support.IsoFixture;

namespace Lyra.Metadata.Tests.Iso;

/// <summary>The track tables, read from their bodies, with every field where its version puts it.</summary>
public class IsoSampleTablesTests
{
    private static byte[] Body(byte[] box) => box[8..];

    [Fact]
    public void ATrackHeaderGivesItsIdAndTimeInEitherVersion()
    {
        byte[] narrow = [0, 0, 0, 0, .. U32(3_000_000_000), .. U32(0), .. U32(7), .. new byte[8]];
        byte[] wide = [1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, .. new byte[8], .. U32(9), .. new byte[4]];

        Assert.Equal(new IsoTrackHeader(7, 3_000_000_000), IsoSampleTables.TrackHeader(narrow));
        Assert.Equal(new IsoTrackHeader(9, 1L << 32), IsoSampleTables.TrackHeader(wide));
        Assert.Null(IsoSampleTables.TrackHeader(wide[..20]));
    }

    [Fact]
    public void ASampleEntryGivesItsTypeAndAVisualOnesSize()
    {
        var visual = new byte[86];
        visual[32 - 8] = 0x15;
        visual[33 - 8] = 0xC0; // 5568 wide
        visual[35 - 8] = 0xE0; // 224 high
        var stsd = Body(FullBox("stsd", 0, U32(1), Box("CRAW", visual)));

        Assert.Equal(new IsoSampleEntry("CRAW", 5568, 224, null), IsoSampleTables.SampleEntry(stsd));
        Assert.Equal(new IsoSampleEntry("CTMD", 0, 0, null), IsoSampleTables.SampleEntry(Body(FullBox("stsd", 0, U32(1), Box("CTMD", new byte[8])))));
        Assert.Null(IsoSampleTables.SampleEntry(stsd[..20]));
    }

    [Fact]
    public void TheFirstSampleAndChunkAreFound()
    {
        Assert.Equal(500u, IsoSampleTables.FirstSampleSize(Body(FullBox("stsz", 0, U32(500), U32(3)))[..12].Concat(new byte[4]).ToArray()));
        Assert.Equal(42u, IsoSampleTables.FirstSampleSize(Body(FullBox("stsz", 0, U32(0), U32(2), U32(42), U32(43)))));
        Assert.Equal(0x1234L, IsoSampleTables.FirstChunkOffset(Body(FullBox("stco", 0, U32(1), U32(0x1234))), large: false));
        Assert.Equal(1L << 33, IsoSampleTables.FirstChunkOffset(Body(FullBox("co64", 0, U32(1), U32(2), U32(0))), large: true));
        Assert.Null(IsoSampleTables.FirstChunkOffset(Body(FullBox("co64", 0, U32(1), U32(2))), large: true));
    }
}