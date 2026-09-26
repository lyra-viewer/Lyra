using Lyra.Common;
using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Decoders.Tiff;
using Lyra.Imaging.Decoding.Structure;
using Lyra.Imaging.Interop;
using Lyra.Imaging.Metadata;
using SkiaSharp;

namespace Lyra.Imaging.Decoding.Decoders;

/// <summary>
/// Decodes TIFF images via the native libtiff wrapper. Which way a directory is read depends on
/// its layout and size:
/// <list type="bullet">
/// <item><see cref="TiffWholeImage"/> - libtiff's RGBA interface, for anything it accepts that fits one bitmap.</item>
/// <item><see cref="TiffRegion"/> - by region at the directory's own depth, for sheets too large for RGBA, streamed when too large to hold at all.</item>
/// <item><see cref="TiffNativeLayout"/> - at the directory's own sample layout, for what the RGBA interface refuses.</item>
/// <item><see cref="TiffDocument"/> - a file of several pages, each read one of the ways above.</item>
/// </list>
/// Every read that touches the file goes through <see cref="TiffReads"/>.
/// </summary>
internal sealed class TiffDecoder : DecoderBase, IThumbnailDecoder
{
    public override bool CanDecode(ImageFormatType format) => format is ImageFormatType.Tiff;

    protected override void Decode(Composite composite, string path, CancellationToken ct)
    {
        var directories = TiffNative.DescribeDirectories(path, IntPtr.Zero, 0, out var encodedBytes);
        var pages = TiffPageSet.Pages(directories);

        if (pages.Count > 0 && directories.Count > pages[0])
            composite.ReportPixelCount((long)directories[pages[0]].Width, (long)directories[pages[0]].Height);

        if (pages.Count > 1)
        {
            TiffDocument.Decode(path, composite, directories, encodedBytes, pages, ct);
            return;
        }

        var page = pages.Count > 0 ? pages[0] : 0;
        var info = directories.Count > page ? directories[page] : default;

        TiffPageSet.Describe(composite, directories, pages, BigTiffMetadataReader.IsBigTiff(path));

        if (TiffRoute.For(info) is TiffReadPath.NativeLayout or TiffReadPath.RegionStreamed)
            composite.ExifInfo = MetadataProcessor.ParseMetadata(path);

        composite.Content = TiffRoute.Decode(path, page, info, composite,
            readWhole: () => TiffWholeImage.Read(path, composite, ct, out _),
            ct: ct,
            fetch: () => TiffFetch.IntoMemory(path, composite, ct));

        if (composite.ExifInfo is null)
            Interlocked.CompareExchange(ref composite.ExifInfo, MetadataProcessor.ParseMetadata(path), null);

        if (directories.Count > 1)
            composite.Structure = [TiffPageSet.Summarise(directories, pages)];
    }

    public SKBitmap? DecodeThumbnail(string path, int maxDimension, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var directories = TiffNative.DescribeDirectories(path, IntPtr.Zero, 0);
        var pages = TiffPageSet.Pages(directories);
        var page = pages.Count > 0 ? pages[0] : 0;
        var info = directories.Count > page ? directories[page] : default;

        return TiffRoute.Thumbnail(path, page, info, maxDimension, () => TiffWholeImage.Read(path, composite: null, ct, out _), ct);
    }
}
