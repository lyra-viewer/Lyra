using Lyra.Common;
using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Decoders.Animation;
using Lyra.Imaging.Decoding.Structure;
using Lyra.Imaging.Decoding.Support;
using Lyra.Imaging.Metadata;
using Lyra.ManagedCodecs.Raster.Png;
using SkiaSharp;

namespace Lyra.Imaging.Decoding.Decoders;

/// <summary>
/// Still PNGs take the plain Skia path. Skia does not read APNG, so an animated one is
/// composited here frame by frame; one whose animation cannot be read falls back to its default
/// image, as any decoder without APNG support shows it.
/// </summary>
internal sealed class PngDecoder : SkiaDecoder
{
    public override bool CanDecode(ImageFormatType format) => format is ImageFormatType.Png;

    protected override bool TryDecodeAnimation(Composite composite, Stream stream, string path, CancellationToken ct)
    {
        stream.Position = 0;
        if (!ApngChunkReader.DeclaresAnimation(stream))
            return false;

        var bytes = DecoderIO.ReadToEnd(stream, ct);
        var chunks = ApngChunkReader.Read(bytes);

        if (chunks is not { Frames.Count: > 0 })
        {
            Logger.Warning($"[PngDecoder] {Path.GetFileName(path)} declares an animation with no readable frames; showing its default image.");

            if (chunks is not null)
                ApngFrameSet.Describe(composite, chunks);

            return false;
        }

        using (var metadata = new MemoryStream(bytes, writable: false))
            composite.ExifInfo = MetadataProcessor.ParseMetadata(metadata, path);

        composite.ReportPixelCount(chunks.Width, chunks.Height);
        DecoderValidation.RequireSaneDimensions(chunks.Width, chunks.Height);
        DecoderValidation.RequireAvailableMemory(chunks.Width, chunks.Height);

        ApngFrameSet.Describe(composite, chunks);

        ct.ThrowIfCancellationRequested();

        var damage = new FrameDamageLog();
        var info = AnimatedContent.CanvasInfo(chunks.Width, chunks.Height, ColorSpaceOf(bytes));
        var renderer = new ApngFrameRenderer(AnimatedContent.Opener(bytes, path), chunks, info, damage.Report);
        var source = new RasterFrameSource(renderer.Count, renderer.Render, renderer, composite);

        AnimatedContent.Publish(composite, path, source, damage, () => ApngFrameSet.Describe(chunks), "APNG", ct);
        return true;
    }

    private static SKColorSpace? ColorSpaceOf(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var codec = SKCodec.Create(stream);
        return codec?.Info.ColorSpace;
    }
}