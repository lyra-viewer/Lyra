using Lyra.Imaging.Decoding.Decoders;
using Lyra.Imaging.Interop;
using Lyra.Imaging.Tests.Support;
using Xunit;

namespace Lyra.Imaging.Tests.Interop;

public class RawDevelopStageTests
{
    private static readonly Lazy<bool> NativeRawReady = new(() => NativeWrapper.TryLoad("libraw_native", typeof(RawDecoder).Assembly));

    private static void RequireNative()
    {
        if (!NativeRawReady.Value)
            Assert.Skip("libraw_native not available (native wrappers not built for this platform).");
    }

    [Fact]
    public void ADevelop_IsIdleBefore_AndFinishingAfter()
    {
        RequireNative();

        using var file = new TempFile(DngBuilder.Build(new DngBuilder.Spec()));
        using var image = RawNative.OpenFile(file.Path, out _);
        Assert.NotNull(image);

        Assert.Equal(RawNative.StageIdle, RawNative.raw_develop_stage(image));
        Assert.True(Develop(image));
        Assert.Equal(RawNative.StageFinishing, RawNative.raw_develop_stage(image));
    }

    [Fact]
    public void ADevelopCancelledEarly_NeverReportsFinishing()
    {
        RequireNative();

        using var file = new TempFile(DngBuilder.Build(new DngBuilder.Spec()));
        using var image = RawNative.OpenFile(file.Path, out _);
        Assert.NotNull(image);

        RawNative.raw_cancel(image);
        Assert.False(Develop(image));
        Assert.Equal(RawNative.StageEarly, RawNative.raw_develop_stage(image));
    }

    [Fact]
    public void AClosedHandle_Throws_RatherThanReadingFreedMemory()
    {
        RequireNative();

        using var file = new TempFile(DngBuilder.Build(new DngBuilder.Spec()));
        var image = RawNative.OpenFile(file.Path, out _);
        Assert.NotNull(image);

        image.Dispose();
        Assert.Throws<ObjectDisposedException>(() => RawNative.raw_develop_stage(image));
    }

    private static bool Develop(RawNative.ImageHandle image)
    {
        var developed = RawNative.raw_develop(image, new RawNative.DevelopOptions { Demosaic = RawNative.DemosaicAhd }, out var pixels);

        if (pixels.Owner != IntPtr.Zero)
            RawNative.free_raw_pixels(pixels.Owner);

        return developed && pixels.Data != IntPtr.Zero;
    }
}