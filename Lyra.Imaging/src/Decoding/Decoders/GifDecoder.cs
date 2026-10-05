using Lyra.Common;
using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Decoders.Animation;
using Lyra.Imaging.Decoding.Structure;
using Lyra.Imaging.Decoding.Support;
using Lyra.Imaging.Metadata;
using Lyra.ManagedCodecs.Raster.Gif;
using SkiaSharp;

namespace Lyra.Imaging.Decoding.Decoders;

internal sealed class GifDecoder : SkiaDecoder
{
    public override bool CanDecode(ImageFormatType format) => format is ImageFormatType.Gif;

    protected override void Decode(Composite composite, string path, CancellationToken ct)
    {
        var bytes = composite.ReadAllBytes(ct);

        using (var metadata = new MemoryStream(bytes, writable: false))
            composite.ExifInfo = MetadataProcessor.ParseMetadata(metadata, path);

        var blocks = GifBlockReader.Read(bytes);

        using var stream = new MemoryStream(bytes, writable: false);
        using var codec = SKCodec.Create(stream);

        if (codec is null)
        {
            GifFrameSet.Describe(composite, [], blocks);
            throw new LoadFailureException(LoadFailureKind.DecodeFailed, WhyNoImage(blocks));
        }

        var (width, height) = (codec.Info.Width, codec.Info.Height);

        composite.ReportPixelCount(width, height);
        DecoderValidation.RequireSaneDimensions(width, height);
        DecoderValidation.RequireAvailableMemory(width, height);

        var frames = codec.FrameInfo;

        GifFrameSet.Describe(composite, frames, blocks);

        if (frames.Length == 0)
            throw new LoadFailureException(LoadFailureKind.DecodeFailed, WhyNoImage(blocks));

        ct.ThrowIfCancellationRequested();

        var open = AnimatedContent.Opener(bytes, path);

        AnimatedContent.Publish(
            composite, path, codec, open,
            () => GifFrameSet.Describe(frames, blocks, width, height),
            Explainer(blocks, frames.Length, open),
            "GIF",
            ct);
    }

    /// <summary>Why Skia found nothing to decode, as far as the block walk can tell.</summary>
    private static string WhyNoImage(GifBlocks? blocks) => blocks switch
    {
        null => "The file has no valid GIF header",
        { Frames.Count: 0, Truncated: true } => "The GIF ends before its first frame",
        { Frames.Count: 0 } => "The GIF holds no frames",
        _ => "No frame in the GIF could be read"
    };
    
    /// <summary>
    /// Says why a frame Skia rejected could not be decoded. Only consulted on failure, so a healthy
    /// file never pays for reading its pixel data twice. Null when the block walk and Skia disagree
    /// on the frames, since its frame numbers would then not match.
    /// </summary>
    private static Func<int, string?>? Explainer(GifBlocks? blocks, int frameCount, Func<Stream> open)
    {
        if (blocks is null || blocks.Frames.Count != frameCount)
            return null;

        return index =>
        {
            var frame = blocks.Frames[index];

            try
            {
                using var stream = open();
                var data = new byte[frame.DataLength];

                stream.Position = frame.DataOffset;
                stream.ReadExactly(data);

                return GifFrameSet.ExplainPixelData(GifPixelData.Check(data, frame), index);
            }
            catch (Exception ex)
            {
                Logger.Warning($"[GifDecoder] Could not read frame {index + 1}'s data to explain its failure: {ex.Message}");
                return null;
            }
        };
    }
}
