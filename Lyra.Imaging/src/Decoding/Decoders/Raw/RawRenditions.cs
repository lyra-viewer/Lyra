using Lyra.Imaging.Content;
using Lyra.Imaging.Content.Variants;
using Lyra.Imaging.Decoding.Support;
using Lyra.Imaging.Interop;
using Lyra.Imaging.Loading;
using Lyra.Imaging.Content.Status;

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

    private readonly DecodeContext _context;
    private readonly string _path;
    private readonly RawNative.PreviewEntry[] _previews;
    private VariantContainer? _set;

    private RawRenditions(DecodeContext context, string path, RawNative.PreviewEntry[] previews)
    {
        _context = context;
        _path = path;
        _previews = previews;
    }

    /// <summary>What the renditions of one raw may keep decoded at once: half the decoded-image cache.</summary>
    private static long Budget => ImageLoader.CacheBudgetBytes / 2;

    /// <summary>The renditions, with <paramref name="standIn"/> in the developed image's place until it arrives.</summary>
    public static VariantContainer WithStandIn(DecodeContext context, string path, RawNative.ImageHandle image, RawNative.RawInfo info, ICompositeContent standIn)
    {
        var previews = RawNative.ListPreviews(image);
        return Build(context, path, previews, info.Flip, DevelopedVariant(context, info.Width, info.Height), standIn);
    }

    /// <summary>
    /// The renditions around an image developed without a stand-in, or the developed image alone
    /// when the camera embedded nothing else to pick.
    /// </summary>
    public static ICompositeContent Around(DecodeContext context, string path, RawDecoder.DevelopedImage developed)
    {
        using var image = RawNative.OpenFile(path, out var info);
        var previews = image is null ? [] : RawNative.ListPreviews(image);

        return previews.Length == 0
            ? developed.Content
            : Build(context, path, previews, info.Flip, DescribeDeveloped(context, developed), developed.Content);
    }

    public static ImageVariant DescribeDeveloped(DecodeContext context, RawDecoder.DevelopedImage developed) => DevelopedVariant(context, developed.Width, developed.Height);

    private static VariantContainer Build(DecodeContext context, string path, RawNative.PreviewEntry[] previews, int flip, ImageVariant developed, ICompositeContent first)
    {
        var provider = new RawRenditions(context, path, previews);
        var set = new VariantContainer([developed, .. previews.Select(preview => PreviewVariant(preview, flip))], Developed, first, provider, Budget);

        provider._set = set;
        return set;
    }

    private static ImageVariant DevelopedVariant(DecodeContext context, int width, int height) => new("Developed", width, height, $"{width} x {height}", context.Source.Length);

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
            return RawDecoder.DevelopFile(_context, _path, warning => _set?.RecordWarning(Developed, warning), ct).Content;

        var entry = _previews[index - 1];

        using var image = RawNative.OpenFile(_path, out var info) ?? throw NativeErrors.DecodeFailed(RawNative.get_last_raw_error(), _path);

        var bitmap = RawDecoder.DecodePreviewAt(image, info, entry.Index, ct)
                     ?? throw new LoadFailureException(LoadFailureKind.DecodeFailed, "The camera's preview could not be decoded");

        // The builder takes ownership of the bitmap.
        return RasterContentBuilder.Build(bitmap, _context);
    }
}
