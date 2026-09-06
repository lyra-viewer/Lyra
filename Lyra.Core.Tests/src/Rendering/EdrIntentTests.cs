using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Lyra.Common.Settings.Enums;
using Lyra.Renderer.Drawing;
using SkiaSharp;
using Xunit;

namespace Lyra.Core.Tests.Rendering;

/// <summary>
/// The EDR rendering intent: the same picture with its highlights allowed to use whatever the panel
/// grants. ACES, Reinhard and Clip are the SDR intent - what you get with no headroom, on an SDR
/// display, or once headroom runs out - and EDR spends what is offered on top.
/// </summary>
public class EdrIntentTests
{
    private static readonly SKColorSpace LinearSrgb = SKColorSpace.CreateSrgbLinear();

    /// <summary>
    /// A panel granting no headroom - every panel for the first second, every SDR panel always -
    /// renders exactly what the SDR intent renders. This is what lets the surface stay extended
    /// all the time.
    /// </summary>
    [Theory]
    [InlineData(0.25f)]
    [InlineData(0.5f)]
    [InlineData(0.9f)]
    [InlineData(4f)]
    public void WithNoHeadroom_EdrIsExactlyTheSdrRendering(float scene)
    {
        var sdr = Render(scene, SurfaceProfile.DisplayReferred(LinearSrgb));
        var extendedButFlat = Render(scene, SurfaceProfile.Extended(LinearSrgb, headroom: 1f));

        Assert.Equal(sdr, extendedButFlat, 3);
    }

    [Theory]
    [InlineData(0.1f)]
    [InlineData(0.25f)]
    [InlineData(0.4f)]
    public void HeadroomLeavesTheDiffuseRangeAlone(float scene)
    {
        var withoutHeadroom = Render(scene, SurfaceProfile.Extended(LinearSrgb, headroom: 1f));
        var withHeadroom = Render(scene, SurfaceProfile.Extended(LinearSrgb, headroom: 4.8f));

        Assert.Equal(withoutHeadroom, withHeadroom, 3);
    }

    [Fact]
    public void HighlightsClimbIntoTheHeadroom()
    {
        var clamped = Render(8f, SurfaceProfile.Extended(LinearSrgb, headroom: 1f));
        var spent = Render(8f, SurfaceProfile.Extended(LinearSrgb, headroom: 4.8f));

        Assert.Equal(SurfaceProfile.SdrWhite, clamped, 3);
        Assert.True(spent > 2f, $"a bright highlight should climb well past white, got {spent}.");
        Assert.True(spent <= 4.8f + 0.01f, $"and never past the panel's headroom, got {spent}.");
    }
    
    [Fact]
    public void MoreHeadroomIsMoreHighlight()
    {
        var readings = new[] { 1f, 1.5f, 2.5f, 3.5f, 4.8f }
            .Select(headroom => Render(6f, SurfaceProfile.Extended(LinearSrgb, headroom)))
            .ToArray();

        for (var i = 1; i < readings.Length; i++)
            Assert.True(readings[i] > readings[i - 1], $"headroom step {i} did not brighten: {readings[i - 1]} then {readings[i]}.");
    }

    [Theory]
    [InlineData(2f)]
    [InlineData(4.805f)]
    [InlineData(16f)]
    public void NothingExceedsTheGrantedHeadroom(float headroom)
    {
        var reading = Render(1000f, SurfaceProfile.Extended(LinearSrgb, headroom));

        Assert.True(reading <= headroom + 0.01f, $"got {reading} against a headroom of {headroom}.");
    }

    [Fact]
    public void ADisplayReferredSurfaceIsUnaffectedByHeadroom()
    {
        Assert.Equal(SurfaceProfile.SdrWhite, Render(50f, SurfaceProfile.DisplayReferred(LinearSrgb)), 3);
        Assert.Equal(SurfaceProfile.SdrWhite, new SurfaceProfile(LinearSrgb, false, 16f).Ceiling);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(0.5f)]
    [InlineData(-3f)]
    public void AnImpossibleHeadroomStillMeansAtLeastWhite(float headroom)
    {
        Assert.Equal(SurfaceProfile.SdrWhite, SurfaceProfile.Extended(LinearSrgb, headroom).Ceiling);
    }

    private static float Render(float scene, SurfaceProfile surface)
    {
        using var image = LinearPixel(scene);
        using var paint = HdrToneMapShader.CreatePaint(image, new SKSamplingOptions(SKFilterMode.Nearest), SKMatrix.CreateIdentity(), ToneMapMode.Aces, exposureScale: 1f, whitePoint: 4f, surface);

        Assert.NotNull(paint);

        var info = new SKImageInfo(1, 1, SKColorType.RgbaF16, SKAlphaType.Unpremul, LinearSrgb);
        using var target = SKSurface.Create(info);
        target.Canvas.DrawRect(new SKRect(0, 0, 1, 1), paint);

        var buffer = new byte[8];
        using var snapshot = target.Snapshot();
        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);

        try
        {
            Assert.True(snapshot.ReadPixels(info, handle.AddrOfPinnedObject(), 8, 0, 0));
        }
        finally
        {
            handle.Free();
        }

        return (float)BinaryPrimitives.ReadHalfLittleEndian(buffer);
    }

    private static SKImage LinearPixel(float value)
    {
        var info = new SKImageInfo(1, 1, SKColorType.RgbaF16, SKAlphaType.Unpremul, LinearSrgb);
        var bitmap = new SKBitmap(info);

        var pixel = new byte[8];
        for (var channel = 0; channel < 3; channel++)
            BitConverter.GetBytes((Half)value).CopyTo(pixel, channel * 2);

        BitConverter.GetBytes((Half)1f).CopyTo(pixel, 6);

        Marshal.Copy(pixel, 0, bitmap.GetPixels(), pixel.Length);
        bitmap.SetImmutable();

        return SKImage.FromBitmap(bitmap);
    }
}