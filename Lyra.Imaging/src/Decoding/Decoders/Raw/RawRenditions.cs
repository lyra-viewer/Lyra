using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Support;
using Lyra.Imaging.Interop;
using Lyra.Imaging.Loading;

namespace Lyra.Imaging.Decoding.Decoders.Raw;

/// <summary>
/// A camera raw's renditions: the developed image first, then the previews the camera embedded,
/// largest first. Decodes the ones not resident when they are picked.
/// </summary>
internal sealed class RawRenditions : IVariantProvider
{
    public const int Developed = 0;

    /// <summary>Below this on its longer side, a preview is called a thumbnail.</summary>
    private const int ThumbnailEdge = 640;

    private readonly Composite _composite;
    private readonly string _path;
    private readonly RawNative.PreviewEntry[] _previews;
    private VariantRasterContent? _set;

    private RawRenditions(Composite composite, string path, RawNative.PreviewEntry[] previews)
    {
        _composite = composite;
        _path = path;
        _previews = previews;
    }

    /// <summary>What the renditions of one raw may keep decoded at once: half the decoded-image cache.</summary>
    private static long Budget => ImageLoader.CacheBudgetBytes / 2;

    /// <summary>The renditions, with <paramref name="standIn"/> in the developed image's place until it arrives.</summary>
    public static VariantRasterContent WithStandIn(Composite composite, string path, RawNative.ImageHandle image, RawNative.RawInfo info, ICompositeContent standIn)
    {
        var previews = RawNative.ListPreviews(image);
        return Build(composite, path, previews, info.Flip, DevelopedVariant(composite, info.Width, info.Height), standIn);
    }

    /// <summary>
    /// The renditions around an image developed without a stand-in, or the developed image alone
    /// when the camera embedded nothing else to pick.
    /// </summary>
    public static ICompositeContent Around(Composite composite, string path, RawDecoder.DevelopedImage developed)
    {
        using var image = RawNative.OpenFile(path, out var info);
        var previews = image is null ? [] : RawNative.ListPreviews(image);

        return previews.Length == 0
            ? developed.Content
            : Build(composite, path, previews, info.Flip, DescribeDeveloped(composite, developed), developed.Content);
    }

    public static ImageVariant DescribeDeveloped(Composite composite, RawDecoder.DevelopedImage developed) => DevelopedVariant(composite, developed.Width, developed.Height);

    private static VariantRasterContent Build(Composite composite, string path, RawNative.PreviewEntry[] previews, int flip, ImageVariant developed, ICompositeContent first)
    {
        var provider = new RawRenditions(composite, path, previews);
        var set = new VariantRasterContent([developed, .. previews.Select(preview => PreviewVariant(preview, flip))], Developed, first, provider, Budget);

        provider._set = set;
        return set;
    }

    private static ImageVariant DevelopedVariant(Composite composite, int width, int height) => new("Developed", width, height, $"{width} x {height}", composite.FileSizeBytes);

    private static ImageVariant PreviewVariant(RawNative.PreviewEntry preview, int flip)
    {
        // Stored as the sensor sees it; shown turned like the raw.
        var swaps = (flip & 4) != 0;
        var width = swaps ? preview.Height : preview.Width;
        var height = swaps ? preview.Width : preview.Height;

        var label = Math.Max(width, height) < ThumbnailEdge ? "Camera thumbnail" : "Camera preview";
        var format = preview.Format == RawNative.PreviewJpeg ? "JPEG" : "RGB";

        return new ImageVariant(label, width, height, $"{width} x {height} {format}", preview.Size > 0 ? preview.Size : null);
    }

    public ICompositeContent Decode(int index, CancellationToken ct)
    {
        if (index == Developed)
            return RawDecoder.DevelopFile(_composite, _path, warning => _set?.RecordWarning(Developed, warning), ct).Content;

        var entry = _previews[index - 1];

        using var image = RawNative.OpenFile(_path, out var info) ?? throw NativeErrors.DecodeFailed(RawNative.get_last_raw_error(), _path);

        var bitmap = RawDecoder.DecodePreviewAt(image, info, entry.Index, ct)
                     ?? throw new InvalidOperationException("The camera's preview could not be decoded");

        // The builder takes ownership of the bitmap.
        return RasterContentBuilder.Build(bitmap, _composite);
    }
}
