using Lyra.Common;
using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Decoders.Animation;
using Lyra.Imaging.Decoding.Structure;
using Lyra.Imaging.Decoding.Support;
using Lyra.Imaging.Metadata;
using Lyra.ManagedCodecs.Raster.Webp;
using SkiaSharp;

namespace Lyra.Imaging.Decoding.Decoders;

/// <summary>Still WebPs take the plain Skia path; an animated one is published frame by frame.</summary>
internal sealed class WebpDecoder : SkiaDecoder
{
    public override bool CanDecode(ImageFormatType format) => format is ImageFormatType.Webp;

    protected override bool TryDecodeAnimation(Composite composite, Stream stream, string path, CancellationToken ct)
    {
        Span<byte> header = stackalloc byte[WebpChunkReader.HeaderLength];

        stream.Position = 0;
        var read = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);

        if (!WebpChunkReader.DeclaresAnimation(header[..read]))
            return false;

        var bytes = DecoderIO.ReadToEnd(stream, ct);

        using (var metadata = new MemoryStream(bytes, writable: false))
            composite.ExifInfo = MetadataProcessor.ParseMetadata(metadata, path);

        var chunks = WebpChunkReader.Read(bytes);

        using var memory = new MemoryStream(bytes, writable: false);
        using var codec = SKCodec.Create(memory);

        if (codec is null)
        {
            WebpFrameSet.Describe(composite, [], chunks);
            throw new LoadFailureException(LoadFailureKind.DecodeFailed, WebpFrameSet.WhyNoImage(chunks));
        }

        var (width, height) = (codec.Info.Width, codec.Info.Height);

        composite.ReportPixelCount(width, height);
        DecoderValidation.RequireSaneDimensions(width, height);
        DecoderValidation.RequireAvailableMemory(width, height);

        var frames = codec.FrameInfo;

        WebpFrameSet.Describe(composite, frames, chunks);

        if (frames.Length == 0)
            throw new LoadFailureException(LoadFailureKind.DecodeFailed, WebpFrameSet.WhyNoImage(chunks));

        ct.ThrowIfCancellationRequested();

        AnimatedContent.Publish(
            composite, path, codec,
            AnimatedContent.Opener(bytes, path),
            () => WebpFrameSet.Describe(frames, chunks, width, height),
            explain: null,
            "WebP",
            ct);

        return true;
    }
}