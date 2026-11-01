using Lyra.Common;
using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Decoders.Animation;
using Lyra.Imaging.Decoding.Structure;
using Lyra.Imaging.Decoding.Support;
using Lyra.Imaging.Interop;
using Lyra.Imaging.Metadata;
using Lyra.Metadata.Containers;
using SkiaSharp;

namespace Lyra.Imaging.Decoding.Decoders;

internal class JxlDecoder : DecoderBase
{
    private static readonly SKColorSpace SdrColorSpace =
        SKColorSpace.CreateRgb(SKColorSpaceTransferFn.Srgb, SKColorSpaceXyz.DisplayP3);

    public override IReadOnlyList<ImageFormatType> Formats { get; } = [ImageFormatType.Jxl];

    protected override void Decode(DecodeContext context, CancellationToken ct)
    {
        var path = context.Source.Label;
        var data = context.ReadAllBytes(ct);

        if (JxlContainer.Read(data) is { IsEmpty: false } metadata)
            context.ExifInfo = MetadataProcessor.ParseMetadata(metadata, path);

        ct.ThrowIfCancellationRequested();

        if (TryDecodeAnimation(context, path, data, ct))
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

                    context.ReportPixelCount(width, height);

                    ct.ThrowIfCancellationRequested();

                    context.AddFormatSpecific("Bit Depth", $"{bitsPerSample}-bit");
                    context.AddFormatSpecific("HDR", isHdr != 0);
                    context.AddFormatSpecific("Has Alpha", hasAlpha != 0);
                    context.AddFormatSpecific("Animated", hasAnimation != 0);

                    context.Content = BuildContent(nativePixels, width, height, isHdr != 0, SdrColorSpace, context, ct, out var facts);
                    facts.Describe(context);
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
    private static bool TryDecodeAnimation(DecodeContext context, string path, byte[] data, CancellationToken ct)
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
            context.ReportPixelCount(info.Width, info.Height);

            durations = new int[info.FrameCount];
            if (!JxlNative.jxl_animation_frame_durations(handle, durations, durations.Length))
                Array.Clear(durations);

            context.AddFormatSpecific("Bit Depth", $"{info.BitsPerSample}-bit");
            context.AddFormatSpecific("HDR", info.IsHdr != 0);
            context.AddFormatSpecific("Has Alpha", info.HasAlpha != 0);
            context.AddFormatSpecific("Frames", info.FrameCount.ToString());
            context.AddFormatSpecific("Duration", FrameFacts.TotalDuration(durations));
            context.AddFormatSpecific("Loop", FrameFacts.DescribePlays(info.LoopCount));

            if (info.Incomplete == JxlNative.AnimationTruncated)
                context.AddFormatSpecific("Truncated", "stops before the animation ends");
            else if (info.Incomplete == JxlNative.AnimationBroken)
                context.AddFormatSpecific("Damaged", $"the stream breaks after frame {info.FrameCount}");

            ct.ThrowIfCancellationRequested();
        }
        catch
        {
            handle.Dispose();
            throw;
        }

        var damage = new FrameDamageLog();

        AnimatedContent.Publish(
            context, path,
            new JxlFrameSource(handle, info, colorSpace, context, damage.Report),
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
    internal static unsafe ICompositeContent BuildContent(IntPtr pixels, int width, int height, bool isHdr, SKColorSpace sdrColorSpace, DecodeContext context, CancellationToken ct, out PixelFacts facts) =>
        isHdr ? HdrImageBuilder.Build(new Span<float>((byte*)pixels, checked(width * height * 4)), width, height, context, ct, out facts)
              : BuildSdr((byte*)pixels, width, height, sdrColorSpace, context, ct, out facts);

    private static unsafe ICompositeContent BuildSdr(byte* src, int width, int height, SKColorSpace colorSpace, DecodeContext context, CancellationToken ct, out PixelFacts facts)
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

        return RasterContentBuilder.Build(bitmap, context);
    }
}
