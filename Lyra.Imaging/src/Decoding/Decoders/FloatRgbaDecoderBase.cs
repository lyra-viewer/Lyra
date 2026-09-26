using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Support;

namespace Lyra.Imaging.Decoding.Decoders;

internal abstract class FloatRgbaDecoderBase : DecoderBase
{
    /// <summary>
    /// Loads RGBA float pixels for <paramref name="composite"/>'s file. Implementations throw on
    /// failure; the returned buffer is owned by this base and released via its <c>Dispose</c>.
    /// </summary>
    protected abstract FloatImageBuffer LoadPixels(Composite composite, CancellationToken ct);

    protected override void Decode(Composite composite, string path, CancellationToken ct)
    {
        using var pixels = LoadPixels(composite, ct);

        ct.ThrowIfCancellationRequested();

        var width = pixels.Width;
        var height = pixels.Height;
        DecoderValidation.RequireSaneDimensions(width, height, bytesPerPixel: sizeof(float) * 4);

        composite.ReportPixelCount(width, height);
        
        composite.Content = HdrImageBuilder.Build(pixels.AsSpan(), width, height, composite, ct, out var isGrayscale);

        composite.AddFormatSpecific("GrayScale", isGrayscale.ToString());
    }
}