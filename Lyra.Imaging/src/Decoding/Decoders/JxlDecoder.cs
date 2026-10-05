using Lyra.Common;
using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Decoders.Animation;
using Lyra.Imaging.Decoding.Structure;
using Lyra.Imaging.Decoding.Support;
using Lyra.Imaging.Interop;
using Lyra.Imaging.Metadata;
using SkiaSharp;

namespace Lyra.Imaging.Decoding.Decoders;

internal class JxlDecoder : DecoderBase
{
    private static readonly SKColorSpace SdrColorSpace =
        SKColorSpace.CreateRgb(SKColorSpaceTransferFn.Srgb, SKColorSpaceXyz.DisplayP3);

    public override bool CanDecode(ImageFormatType format) => format == ImageFormatType.Jxl;

    protected override void Decode(Composite composite, string path, CancellationToken ct)
    {
        var data = composite.ReadAllBytes(ct);

        var metadata = IsoBoxMetadata.ReadJxl(data);
        if (!metadata.IsEmpty)
            composite.ExifInfo = MetadataProcessor.ParseMetadata(metadata.Exif, metadata.Xmp, path);

        ct.ThrowIfCancellationRequested();

        if (TryDecodeAnimation(composite, path, data, ct))
            return;

        var nativePixels = IntPtr.Zero;

        try
        {
            unsafe
            {
                fixed (byte* pData = data)
                {
                    var ok = JxlNative.decode_jxl_from_memory(
                        (IntPtr)pData,
                        (nuint)data.Length,
                        out var width,
                        out var height,
                        out var isHdr,
                        out var bitsPerSample,
                        out var hasAlpha,
                        out var hasAnimation,
                        out nativePixels);

                    if (!ok || nativePixels == IntPtr.Zero)
                    {
                        throw NativeErrors.DecodeFailed(JxlNative.get_last_jxl_error(), path);
                    }

                    DecoderValidation.RequireSaneDimensions(width, height);

                    composite.ReportPixelCount(width, height);

                    ct.ThrowIfCancellationRequested();

                    composite.AddFormatSpecific("Bit Depth", $"{bitsPerSample}-bit");
                    composite.AddFormatSpecific("HDR", isHdr != 0);
                    composite.AddFormatSpecific("Has Alpha", hasAlpha != 0);
                    composite.AddFormatSpecific("Animated", hasAnimation != 0);

                    composite.Content = BuildContent(nativePixels, width, height, isHdr != 0, SdrColorSpace, composite, ct, out var facts);
                    facts.Describe(composite);
                }
            }
        }
        finally
        {
            if (nativePixels != IntPtr.Zero)
                JxlNative.free_jxl_pixels(nativePixels);
        }
    }

    /// <summary>
    /// Publishes an animation frame by frame. False for a still image, and when the native
    /// library cannot open the stream as an animation; the first-frame path then decodes it.
    /// </summary>
    private static bool TryDecodeAnimation(Composite composite, string path, byte[] data, CancellationToken ct)
    {
        JxlNative.AnimationHandle? handle;
        JxlNative.AnimationInfo info;

        unsafe
        {
            fixed (byte* pData = data)
                handle = JxlNative.OpenAnimation((IntPtr)pData, (nuint)data.Length, out info);
        }

        if (handle is null)
            return false;

        if (info.FrameCount <= 1 && info.Incomplete == 0)
        {
            handle.Dispose();
            return false;
        }

        var colorSpace = FrameColorSpace(handle, info, path);
        if (colorSpace is null)
        {
            handle.Dispose();
            return false;
        }

        int[] durations;

        try
        {
            DecoderValidation.RequireSaneDimensions(info.Width, info.Height);
            composite.ReportPixelCount(info.Width, info.Height);

            durations = new int[info.FrameCount];
            if (!JxlNative.jxl_animation_frame_durations(handle, durations, durations.Length))
                Array.Clear(durations);

            composite.AddFormatSpecific("Bit Depth", $"{info.BitsPerSample}-bit");
            composite.AddFormatSpecific("HDR", info.IsHdr != 0);
            composite.AddFormatSpecific("Has Alpha", info.HasAlpha != 0);
            composite.AddFormatSpecific("Frames", info.FrameCount.ToString());
            composite.AddFormatSpecific("Duration", FrameFacts.TotalDuration(durations));
            composite.AddFormatSpecific("Loop", FrameFacts.DescribePlays(info.LoopCount));

            if (info.Incomplete == JxlNative.AnimationTruncated)
                composite.AddFormatSpecific("Truncated", "stops before the animation ends");
            else if (info.Incomplete == JxlNative.AnimationBroken)
                composite.AddFormatSpecific("Damaged", $"the stream breaks after frame {info.FrameCount}");

            ct.ThrowIfCancellationRequested();
        }
        catch
        {
            handle.Dispose();
            throw;
        }

        var damage = new FrameDamageLog();

        AnimatedContent.Publish(
            composite, path,
            new JxlFrameSource(handle, info, colorSpace, composite, damage.Report),
            damage,
            () => FrameFacts.WholeFrames(durations, info.Width, info.Height),
            "JPEG XL",
            ct);

        return true;
    }

    /// <summary>
    /// The color space an animation's frames arrive in: what the still path converts to, or the
    /// file's own when libjxl cannot convert them (see jxl_native.h). Null for an HDR animation in
    /// a space the tone mapper cannot take, which then shows its first frame as before.
    /// </summary>
    private static SKColorSpace? FrameColorSpace(JxlNative.AnimationHandle handle, JxlNative.AnimationInfo info, string path)
    {
        if (info.FileColorSpace == 0)
            return info.IsHdr != 0 ? SKColorSpace.CreateSrgbLinear() : SdrColorSpace;

        var icc = JxlNative.AnimationIcc(handle);
        var own = icc is null ? null : SKColorSpace.CreateIcc(icc);

        if (info.IsHdr != 0)
        {
            if (own is not null && IsLinearSrgb(own))
                return own;

            Logger.Warning($"[JxlDecoder] {Path.GetFileName(path)} is an HDR animation libjxl cannot convert for the tone mapper; showing its first frame.");
            return null;
        }

        if (own is null)
            Logger.Warning($"[JxlDecoder] {Path.GetFileName(path)}'s frames have no usable color profile; treating them as sRGB.");

        return own ?? SKColorSpace.CreateSrgb();
    }

    private static bool IsLinearSrgb(SKColorSpace colorSpace)
    {
        if (!colorSpace.GammaIsLinear)
            return false;

        var own = colorSpace.ToColorSpaceXyz().Values;
        var srgb = SKColorSpaceXyz.Srgb.Values;

        return own.Zip(srgb).All(pair => Math.Abs(pair.First - pair.Second) < 1e-3f);
    }

    /// <summary>Content from the native RGBA pixels: linear float for HDR, straight-alpha 8-bit otherwise.</summary>
    internal static unsafe ICompositeContent BuildContent(IntPtr pixels, int width, int height, bool isHdr, SKColorSpace sdrColorSpace, Composite composite, CancellationToken ct, out PixelFacts facts) =>
        isHdr ? HdrImageBuilder.Build(new Span<float>((byte*)pixels, checked(width * height * 4)), width, height, composite, ct, out facts)
              : BuildSdr((byte*)pixels, width, height, sdrColorSpace, composite, ct, out facts);

    private static unsafe ICompositeContent BuildSdr(byte* src, int width, int height, SKColorSpace colorSpace, Composite composite, CancellationToken ct, out PixelFacts facts)
    {
        // Native hands back tightly packed, straight-alpha RGBA8.
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul, colorSpace);
        var bitmap = new SKBitmap(info);

        try
        {
            PixelCopy.CopyPremultiplyingRgba((IntPtr)src, width * 4, bitmap, ct, out var isGrayscale);
            facts = new PixelFacts(isGrayscale);
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }

        return RasterContentBuilder.Build(bitmap, composite);
    }
}
