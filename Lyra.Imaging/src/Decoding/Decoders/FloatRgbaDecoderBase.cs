using Lyra.Common;
using Lyra.Common.SystemExtensions;
using Lyra.Imaging.Content;
using static System.Threading.Thread;
using Lyra.Imaging.Decoding.Support;

namespace Lyra.Imaging.Decoding.Decoders;

internal abstract class FloatRgbaDecoderBase : IImageDecoder
{
    public abstract bool CanDecode(ImageFormatType format);

    /// <summary>
    /// Loads RGBA float pixels for <paramref name="composite"/>'s file. Implementations throw on
    /// failure; the returned buffer is owned by this base and released via its <c>Dispose</c>.
    /// </summary>
    protected abstract FloatImageBuffer LoadPixels(Composite composite, CancellationToken ct);

    public Task DecodeAsync(Composite composite, CancellationToken ct)
    {
        composite.DecoderName = GetType().Name;
        var path = composite.FileInfo.FullName;
        Logger.Debug($"[{GetType().Name}] [Thread: {CurrentThread.GetNameOrId()}] Decoding: {path}");

        ct.ThrowIfCancellationRequested();

        using var pixels = LoadPixels(composite, ct);

        ct.ThrowIfCancellationRequested();

        var width = pixels.Width;
        var height = pixels.Height;
        DecoderValidation.RequireSaneDimensions(GetType().Name, width, height, bytesPerPixel: sizeof(float) * 4);
        
        var content = HdrImageBuilder.Build(pixels.AsSpan(), width, height, composite, ct, out var isGrayscale);

        composite.AddFormatSpecific("GrayScale", isGrayscale.ToString());

        ct.ThrowIfCancellationRequested();

        composite.Content = content;

        return Task.CompletedTask;
    }
}