using Lyra.Common;
using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Support;
using SkiaSharp;

namespace Lyra.Imaging.Decoding.Decoders.Animation;

/// <summary>
/// Publishes an animation: the first frame at once, and when there are more, the whole set as
/// frames the interface selects and decodes on demand.
/// </summary>
internal static class AnimatedContent
{
    /// <summary>Up to this size a file's bytes stay in memory for later frames.</summary>
    private const long RetainedBytesCeiling = 64L * 1024 * 1024;

    /// <summary>Reopens the file for a later frame: from memory when small, else from disk.</summary>
    public static Func<Stream> Opener(byte[] bytes, string path) =>
        bytes.LongLength <= RetainedBytesCeiling
            ? () => new MemoryStream(bytes, writable: false)
            : () => DecoderIO.OpenRandomAccessRead(path);

    public static SKImageInfo CanvasInfo(int width, int height, SKColorSpace? colorSpace) =>
        new(width, height, SKColorType.Rgba8888, SKAlphaType.Premul, colorSpace ?? SKColorSpace.CreateSrgb());

    /// <summary>An animation Skia composites itself, as it does GIF and WebP.</summary>
    /// <param name="explain">Why a frame Skia rejected is damaged, or null when nothing can say.</param>
    public static void Publish(Composite composite, string path, SKCodec codec, Func<Stream> open, Func<IReadOnlyList<ImageVariant>> describe, Func<int, string?>? explain, string format, CancellationToken ct)
    {
        var damage = new FrameDamageLog();
        var renderer = new AnimationFrameRenderer(CanvasInfo(codec.Info.Width, codec.Info.Height, codec.Info.ColorSpace), explain, damage.Report);

        var frameCount = codec.FrameInfo.Length;
        var source = new RasterFrameSource(frameCount, (index, token) => RenderFresh(open, frameCount, renderer, format, index, token), renderer, composite);

        Publish(composite, path, source, damage, describe, format, ct);
    }

    /// <param name="source">Owned from here on, and disposed with the content.</param>
    /// <param name="damage">The log the source reports damaged frames to, when it reports any.</param>
    /// <param name="describe">The frame list, built only when there is more than one frame.</param>
    /// <param name="format">The format's name, for messages.</param>
    public static void Publish(Composite composite, string path, IFrameSource source, FrameDamageLog? damage, Func<IReadOnlyList<ImageVariant>> describe, string format, CancellationToken ct)
    {
        ICompositeContent? first = null;

        try
        {
            first = source.Decode(0, ct);

            if (source.Count <= 1)
            {
                source.Dispose();
                composite.Warning = damage?.WarningOf(0);
                composite.Content = first;
                return;
            }

            Logger.Info(typeof(AnimatedContent), $"[{format}] {Path.GetFileName(path)} holds {source.Count} frames; decoding the first and the rest on demand.");

            var set = new VariantRasterContent(
                describe(),
                active: 0,
                first,
                new FrameProvider(source),
                DecodePolicy.ResidentPageBudgetBytes)
            {
                Kind = VariantKind.Frames
            };

            damage?.Attach(set);
            composite.Content = set;
        }
        catch
        {
            first?.Dispose();
            source.Dispose();
            throw;
        }
    }

    private sealed class FrameProvider(IFrameSource source) : IVariantProvider, IDisposable
    {
        public ICompositeContent Decode(int index, CancellationToken ct) => source.Decode(index, ct);

        public void Dispose() => source.Dispose();
    }

    private static SKBitmap RenderFresh(Func<Stream> open, int frameCount, AnimationFrameRenderer renderer, string format, int index, CancellationToken ct)
    {
        using var stream = open();
        using var codec = SKCodec.Create(stream)
                          ?? throw new LoadFailureException(LoadFailureKind.DecodeFailed, $"The file can no longer be read as a {format}");

        if (codec.FrameCount != frameCount || codec.Info.Width != renderer.Info.Width || codec.Info.Height != renderer.Info.Height)
            throw new LoadFailureException(LoadFailureKind.DecodeFailed, "The file has changed since it was opened");

        ct.ThrowIfCancellationRequested();

        return renderer.Render(codec, index, ct);
    }
}