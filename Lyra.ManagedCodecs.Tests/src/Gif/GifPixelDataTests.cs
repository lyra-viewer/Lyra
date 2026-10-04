using Lyra.ManagedCodecs.Raster.Gif;
using Xunit;

namespace Lyra.ManagedCodecs.Tests.Gif;

public class GifPixelDataTests
{
    private const int Width = 16, Height = 12;

    /// <summary>The single frame of <paramref name="gif"/> and its pixel data.</summary>
    private static (GifFrameBlock Frame, byte[] Data) OnlyFrame(byte[] gif)
    {
        var frame = Assert.Single(GifBlockReader.Read(gif)!.Frames);
        return (frame, gif.AsSpan(frame.DataOffset, frame.DataLength).ToArray());
    }

    /// <summary>A 2x2 frame at the origin, with no palette of its own, followed by <paramref name="pixelData"/>.</summary>
    private static byte[] RawFrame(params byte[] pixelData) => [0x2C, 0, 0, 0, 0, 2, 0, 2, 0, 0x00, .. pixelData];

    private static GifPixelDataCheck CheckRaw(params byte[] pixelData)
    {
        var (frame, data) = OnlyFrame(new GifBuilder(Width, Height).AddRaw(RawFrame(pixelData)).Build());
        return GifPixelData.Check(data, frame);
    }

    [Fact]
    public void AWholeFrame_HasNoProblem_AcrossClearCodes()
    {
        var (frame, data) = OnlyFrame(new GifBuilder(Width, Height).AddSolid(2).Build());

        Assert.Equal(new GifPixelDataCheck(GifPixelDataProblem.None, Width * Height, Width * Height, 8), GifPixelData.Check(data, frame));
    }

    [Fact]
    public void SurplusPixels_AreNoProblem()
    {
        var (frame, data) = OnlyFrame(new GifBuilder(Width, Height).AddSolid(2).Build());

        Assert.Equal(GifPixelDataProblem.None, GifPixelData.Check(data, frame with { Width = 2, Height = 2 }).Problem);
    }

    [Fact]
    public void AFrameDeclaredLargerThanItsData_HasTooFewPixels()
    {
        var (frame, data) = OnlyFrame(new GifBuilder(Width, Height).AddSolid(2).Build());

        var check = GifPixelData.Check(data, frame with { Width = 32, Height = 32 });

        Assert.Equal(new GifPixelDataCheck(GifPixelDataProblem.TooFewPixels, Width * Height, 1024, 8), check);
    }

    [Fact]
    public void EachProblem_IsToldApart()
    {
        // Two-bit literals: clear is 4 and end 5, read three bits at a time.
        Assert.Equal(new GifPixelDataCheck(GifPixelDataProblem.NoData, 0, 4, 2), CheckRaw(0x02, 0x01, 0x05, 0x00));
        Assert.Equal(new GifPixelDataCheck(GifPixelDataProblem.TooFewPixels, 1, 4, 2), CheckRaw(0x02, 0x01, 0x28, 0x00));
        Assert.Equal(new GifPixelDataCheck(GifPixelDataProblem.Corrupt, 0, 4, 2), CheckRaw(0x02, 0x01, 0x07, 0x00));
        Assert.Equal(new GifPixelDataCheck(GifPixelDataProblem.InvalidCodeSize, 0, 4, 9), CheckRaw(0x09, 0x01, 0x00, 0x00));
    }

    [Fact]
    public void NoDataAtAll_IsNoData()
    {
        var (frame, _) = OnlyFrame(new GifBuilder(Width, Height).AddSolid(2).Build());

        Assert.Equal(new GifPixelDataCheck(GifPixelDataProblem.NoData, 0, Width * Height, -1), GifPixelData.Check([], frame));
    }
}
