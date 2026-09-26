using Lyra.Common.Estimation;
using Lyra.Imaging.Decoding.Decoders.Tiff;
using Lyra.Imaging.Interop;
using Xunit;

namespace Lyra.Imaging.Tests.Decoding;

/// <summary>
/// How many rows a streaming pass reads at a time: what memory allows, and on a slow source what
/// arrives in a couple of seconds - never less than one of the file's own strips or tiles.
/// </summary>
public class TiffBandRowsTests
{
    private const long MB = 1024 * 1024;

    private static readonly TransferEstimate SlowShare = new(LatencyMs: 20, BytesPerMs: 11.0 * MB / 1000);

    private static readonly TransferEstimate FastDisk = new(LatencyMs: 0.1, BytesPerMs: 2000.0 * MB / 1000);

    private static readonly TiffNative.DirectoryInfo TiledColour = new()
    {
        Width = 23390, Height = 33110, IsTiled = 1, TileWidth = 512, TileHeight = 512, RegionSamples = 4
    };

    private const long TiledColourBytesPerRow = 753 * MB / 33110;

    private static readonly TiffNative.DirectoryInfo StrippedColour = new()
    {
        Width = 40000, Height = 35900, RowsPerStrip = 1024, RegionSamples = 4
    };

    private const long StrippedColourBytesPerRow = 40000L * 3;

    [Fact]
    public void WithoutASourceSpeed_MemoryAloneDecides()
    {
        Assert.Equal(2048u, TiffRegion.PreviewBandRowsFor(TiledColour));
        Assert.Equal(2048u, TiffRegion.PreviewBandRowsFor(TiledColour, TiledColourBytesPerRow, source: null));
    }

    [Fact]
    public void AFastSource_LeavesTheMemoryBandAlone()
    {
        Assert.Equal(TiffRegion.PreviewBandRowsFor(TiledColour), TiffRegion.PreviewBandRowsFor(TiledColour, TiledColourBytesPerRow, FastDisk));
    }

    [Fact]
    public void ASlowSource_ShortensTheBand_StillAlignedToTiles()
    {
        var rows = TiffRegion.PreviewBandRowsFor(TiledColour, TiledColourBytesPerRow, SlowShare);

        Assert.Equal(512u, rows);
        Assert.True(SlowShare.MsFor(rows * TiledColourBytesPerRow) < 3000, "A band should arrive in a couple of seconds");
    }

    [Fact]
    public void ASlowSource_NeverCutsBelowOneStrip()
    {
        Assert.Equal(1024u, TiffRegion.PreviewBandRowsFor(StrippedColour, StrippedColourBytesPerRow, SlowShare));
    }

    [Fact]
    public void WithNoLayoutUnit_ASlowSourceMayGoBelowTheDefaultBand()
    {
        var unaligned = StrippedColour with { RowsPerStrip = 0 };

        var rows = TiffRegion.PreviewBandRowsFor(unaligned, StrippedColourBytesPerRow, SlowShare);

        Assert.True(rows < 256, $"Got {rows} rows");
        Assert.True(rows >= 1);
    }
}
