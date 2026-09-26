using Lyra.Common;
using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Structure;
using Lyra.Imaging.Decoding.Support;
using Lyra.Imaging.Interop;
using Lyra.Imaging.Metadata;

namespace Lyra.Imaging.Decoding.Decoders.Tiff;

/// <summary>
/// A TIFF holding more than one page, published as a set of pages with the first decoded and the
/// rest on demand.
/// </summary>
internal static class TiffDocument
{
    public static void Decode(string path, Composite composite, IReadOnlyList<TiffNative.DirectoryInfo> directories, long[]? encodedBytes, List<int> pages, CancellationToken ct)
    {
        Logger.Info($"[TiffDecoder] {Path.GetFileName(path)} holds {pages.Count} pages across {directories.Count} directories; decoding the first and the rest on demand.");

        composite.ExifInfo = MetadataProcessor.ParseMetadata(path);
        composite.Structure = [TiffPageSet.Summarise(directories, pages)];
        TiffPageSet.Describe(composite, directories, pages, BigTiffMetadataReader.IsBigTiff(path));

        ct.ThrowIfCancellationRequested();

        var first = DecodePage(path, pages[0], directories[pages[0]], composite, ct);
        var variants = TiffPageSet.Describe(directories, pages, encodedBytes);

        composite.Content = new VariantRasterContent(variants, active: 0, first, new PageProvider(path, pages, directories, composite), DecodePolicy.ResidentPageBudgetBytes)
        {
            GroupLabel = "PAGES"
        };
    }

    /// <summary>Decodes one page, read in place.</summary>
    private static ICompositeContent DecodePage(string path, int directory, TiffNative.DirectoryInfo info, Composite composite, CancellationToken ct) =>
        TiffRoute.Decode(path, directory, info, composite, () => TiffWholeImage.ReadDirectory(path, directory, info, ct), ct);

    /// <summary>
    /// Supplies pages as the interface asks for them. Holds the path rather than any open handle:
    /// a <c>TIFF*</c> is not thread-safe, and this is called from a background thread each time.
    /// </summary>
    private sealed class PageProvider(string path, List<int> pages, IReadOnlyList<TiffNative.DirectoryInfo> directories, Composite composite)
        : IVariantProvider
    {
        public ICompositeContent Decode(int index, CancellationToken ct) => DecodePage(path, pages[index], directories[pages[index]], composite, ct);
    }
}
