using Lyra.Common;
using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Decoders.Gif;
using Lyra.Imaging.Decoding.Structure;
using Lyra.Imaging.Decoding.Support;
using Lyra.Imaging.Metadata;
using Lyra.ManagedCodecs.Raster.Gif;
using SkiaSharp;

namespace Lyra.Imaging.Decoding.Decoders;

internal sealed class GifDecoder : SkiaDecoder
{
    private const long RetainedBytesCeiling = 64L * 1024 * 1024;

    public override bool CanDecode(ImageFormatType format) => format is ImageFormatType.Gif;

    protected override void Decode(Composite composite, string path, CancellationToken ct)
    {
        var bytes = DecoderIO.ReadAllBytes(path, ct, out var readMs, composite.ReportTransferred);
        composite.CompleteTransfer(bytes.Length, readMs);

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

        Func<Stream> open = bytes.LongLength <= RetainedBytesCeiling
            ? () => new MemoryStream(bytes, writable: false)
            : () => DecoderIO.OpenRandomAccessRead(path);

        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul, codec.Info.ColorSpace ?? SKColorSpace.CreateSrgb());
        var damage = new DamageLog();
        var renderer = new GifFrameRenderer(info, Explainer(blocks, frames.Length, open), damage.Report);
        ICompositeContent? first = null;

        try
        {
            first = RasterContentBuilder.Build(renderer.Render(codec, 0, ct), composite);

            if (frames.Length <= 1)
            {
                renderer.Dispose();
                composite.Warning = damage.WarningOf(0);
                composite.Content = first;
                return;
            }

            Logger.Info($"[GifDecoder] {Path.GetFileName(path)} holds {frames.Length} frames; decoding the first and the rest on demand.");

            var set = new VariantRasterContent(
                GifFrameSet.Describe(frames, blocks, width, height),
                active: 0,
                first,
                new FrameProvider(open, frames.Length, renderer, composite),
                DecodePolicy.ResidentPageBudgetBytes)
            {
                Kind = VariantKind.Frames
            };

            damage.Attach(set);
            composite.Content = set;
        }
        catch
        {
            first?.Dispose();
            renderer.Dispose();
            throw;
        }
    }

    /// <summary>Why Skia found nothing to decode, as far as the block walk can tell.</summary>
    private static string WhyNoImage(GifBlocks? blocks) => blocks switch
    {
        null => "The file has no valid GIF header",
        { Frames.Count: 0, Truncated: true } => "The GIF ends before its first frame",
        { Frames.Count: 0 } => "The GIF holds no frames",
        _ => "No frame in the GIF could be read"
    };
    
    private sealed class DamageLog()
    {
        private readonly Dictionary<int, string> _damaged = new();
        private readonly Lock _gate = new();
        private VariantRasterContent? _frames;

        public void Report(int index, string why)
        {
            lock (_gate)
            {
                _damaged[index] = why;
                _frames?.RecordWarning(index, LoadWarning.PartiallyDecoded(why));
            }
        }

        public LoadWarning? WarningOf(int index)
        {
            lock (_gate)
                return _damaged.TryGetValue(index, out var why) ? LoadWarning.PartiallyDecoded(why) : null;
        }

        public void Attach(VariantRasterContent frames)
        {
            lock (_gate)
            {
                _frames = frames;

                foreach (var (index, why) in _damaged)
                    frames.RecordWarning(index, LoadWarning.PartiallyDecoded(why));
            }
        }
    }

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

    /// <summary>
    /// Supplies frames as the interface asks for them, each from a fresh codec: one is not
    /// thread-safe, and this is called from a background thread each time.
    /// </summary>
    private sealed class FrameProvider(Func<Stream> open, int frameCount, GifFrameRenderer renderer, Composite composite)
        : IVariantProvider, IDisposable
    {
        public ICompositeContent Decode(int index, CancellationToken ct)
        {
            using var stream = open();
            using var codec = SKCodec.Create(stream)
                ?? throw new LoadFailureException(LoadFailureKind.DecodeFailed, "The file can no longer be read as a GIF");

            if (codec.FrameCount != frameCount || codec.Info.Width != renderer.Info.Width || codec.Info.Height != renderer.Info.Height)
                throw new LoadFailureException(LoadFailureKind.DecodeFailed, "The file has changed since it was opened");

            ct.ThrowIfCancellationRequested();

            return RasterContentBuilder.Build(renderer.Render(codec, index, ct), composite);
        }

        public void Dispose() => renderer.Dispose();
    }
}
