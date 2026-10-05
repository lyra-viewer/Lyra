using Lyra.Imaging.Content;
using Lyra.Imaging.Interop;
using SkiaSharp;

namespace Lyra.Imaging.Decoding.Decoders.Animation;

/// <summary>
/// Frames of an animated JPEG XL, which libjxl composites itself. The native handle keeps its
/// decoder positioned after the last frame it produced, so stepping forward costs one frame;
/// calls on it are serialized, as it is not thread-safe.
/// </summary>
internal sealed class JxlFrameSource(JxlNative.AnimationHandle handle, JxlNative.AnimationInfo info, SKColorSpace colorSpace, Composite composite, Action<int, string>? damaged = null)
    : ExclusiveResource, IFrameSource
{
    private readonly HashSet<int> _reported = [];

    public int Count => info.FrameCount;

    public ICompositeContent Decode(int index, CancellationToken ct) => Exclusive(() => DecodeLocked(index, ct));

    private ICompositeContent DecodeLocked(int index, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (index < 0 || index >= info.FrameCount)
            throw new ArgumentOutOfRangeException(nameof(index));

        if (!JxlNative.jxl_animation_decode_frame(handle, index, out var pixels, out var partial))
            throw new LoadFailureException(LoadFailureKind.DecodeFailed, Reason(index));

        try
        {
            if (partial != 0 && _reported.Add(index))
                damaged?.Invoke(index, $"Frame {index + 1} is cut short: the file ends inside it");

            return JxlDecoder.BuildContent(pixels, info.Width, info.Height, info.IsHdr != 0, colorSpace, composite, ct, out _);
        }
        finally
        {
            JxlNative.free_jxl_pixels(pixels);
        }
    }

    private static string Reason(int index)
    {
        var reason = NativeErrors.GetUtf8ZOrAnsiZ(JxlNative.get_last_jxl_error());
        return string.IsNullOrWhiteSpace(reason) ? $"Frame {index + 1} could not be decoded" : reason;
    }

    protected override void Release() => handle.Dispose();
}