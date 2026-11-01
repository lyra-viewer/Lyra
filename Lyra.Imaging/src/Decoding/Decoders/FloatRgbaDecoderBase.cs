using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Support;

namespace Lyra.Imaging.Decoding.Decoders;

internal abstract class FloatRgbaDecoderBase : DecoderBase
{
    /// <summary>
    /// Loads RGBA float pixels for <paramref name="context"/>'s file. Implementations throw on
    /// failure; the returned buffer is owned by this base and released via its <c>Dispose</c>.
    /// </summary>
    protected abstract FloatImageBuffer LoadPixels(DecodeContext context, CancellationToken ct);

    protected override void Decode(DecodeContext context, CancellationToken ct)
    {
        using var pixels = LoadPixels(context, ct);

        ct.ThrowIfCancellationRequested();

        var width = pixels.Width;
        var height = pixels.Height;
        DecoderValidation.RequireSaneDimensions(width, height, bytesPerPixel: sizeof(float) * 4);

        context.ReportPixelCount(width, height);
        
        context.Content = HdrImageBuilder.Build(pixels.AsSpan(), width, height, context, ct, out var facts);

        facts.Describe(context);
    }
}