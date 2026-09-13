using System.Runtime.InteropServices;
using Lyra.Common.SystemExtensions;
using Lyra.Common;
using Lyra.Imaging.ConstraintsProvider;
using Lyra.Imaging.Content.Tiling;
using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Structure;
using Lyra.Imaging.Decoding.Support;
using Lyra.Imaging.Interop;
using Lyra.Imaging.Loading;
using SkiaSharp;
using static System.Threading.Thread;
using Lyra.Imaging.Metadata;

namespace Lyra.Imaging.Decoding.Decoders;

/// <summary>
/// Decodes TIFF images via the native libtiff wrapper. libtiff handles every compression,
/// photometric, planar and tiled variant and returns an 8-bit RGBA raster with
/// <em>premultiplied</em> (associated) alpha, top-left origin. Unassociated-alpha TIFFs are
/// converted by libtiff itself (the UaToAa table in tif_getimage.c), so Premul is correct
/// for both EXTRASAMPLES variants.
/// </summary>
internal sealed class TiffDecoder : IImageDecoder, IThumbnailDecoder
{
    public bool CanDecode(ImageFormatType format) => format is ImageFormatType.Tiff;

    public Task DecodeAsync(Composite composite, CancellationToken ct)
    {
        var path = composite.FileInfo.FullName;
        composite.DecoderName = GetType().Name;
        Logger.Debug($"[TiffDecoder] [Thread: {CurrentThread.GetNameOrId()}] Decoding: {path}");

        try
        {
            ct.ThrowIfCancellationRequested();

            var directories = TiffNative.DescribeDirectories(path, IntPtr.Zero, 0);
            var pages = TiffPageSet.Pages(directories);

            if (pages.Count > 0 && directories.Count > pages[0])
                composite.ReportPixelCount((long)directories[pages[0]].Width, (long)directories[pages[0]].Height);

            if (pages.Count > 1)
            {
                DecodeDocument(path, composite, directories, pages, ct);
                return Task.CompletedTask;
            }

            var page = pages.Count > 0 ? pages[0] : 0;
            var info = directories.Count > page ? directories[page] : default;

            TiffPageSet.Describe(composite, directories, pages, IsBigTiff(path));

            if (info.RgbaCapable == 0 && info.NativeCapable != 0)
            {
                composite.ExifInfo = MetadataProcessor.ParseMetadata(path);
                composite.Content = DecodeUnsupportedLayout(path, page, info, composite, ct);
            }
            else if (WantsNativeDepth(info))
            {
                composite.ExifInfo = MetadataProcessor.ParseMetadata(path);
                composite.Content = WantsStreaming(info)
                    ? StreamGray(path, page, info, composite, ct)
                    : RasterContentBuilder.Build(DecodeGray(path, page, info, ct), composite);
            }
            else
            {
                try
                {
                    var bitmap = LoadBitmap(path, composite, ct, tagColorSpace: true, out var metadataParsed);
                    if (!metadataParsed)
                        composite.ExifInfo = MetadataProcessor.ParseMetadata(path);

                    composite.Content = RasterContentBuilder.Build(bitmap, composite);
                }
                catch (InvalidOperationException) when (info.NativeCapable != 0)
                {
                    Logger.Info($"[TiffDecoder] The RGBA interface accepted {Path.GetFileName(path)} and then refused it; reading it at its own layout instead.");

                    if (composite.ExifInfo is null)
                        composite.ExifInfo = MetadataProcessor.ParseMetadata(path);

                    composite.Content = DecodeUnsupportedLayout(path, page, info, composite, ct);
                }
            }

            if (directories.Count > 1)
                composite.Structure = [TiffPageSet.Summarise(directories, pages)];
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            Logger.Warning($"[TiffDecoder] Image could not be loaded: {path}\n{e.Message}");
            throw;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Whether the file is BigTIFF, from its header rather than from libtiff.
    /// </summary>
    private static bool IsBigTiff(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);

            Span<byte> header = stackalloc byte[4];
            if (stream.ReadAtLeast(header, 4, throwOnEndOfStream: false) < 4)
                return false;

            var little = header[0] == 'I' && header[1] == 'I';
            var big = header[0] == 'M' && header[1] == 'M';

            if (!little && !big)
                return false;

            var version = little
                ? (ushort)(header[2] | (header[3] << 8))
                : (ushort)((header[2] << 8) | header[3]);

            return version == 43;
        }
        catch (Exception ex)
        {
            Logger.Debug($"[TiffDecoder] Could not read the header of {path}: {ex.Message}");
            return false;
        }
    }

    #region Layouts the RGBA interface refuses

    /// <summary>
    /// The most one image may cost while it is read at its own sample layout.
    /// </summary>
    internal static long NativeLayoutBudgetBytes => ImageLoader.CacheBudgetBytes / 2;

    /// <summary>
    /// What reading this directory at its own layout costs at peak: the file's own packing plus
    /// the converted output, which overlap.
    /// </summary>
    internal static long NativeLayoutPeakBytes(TiffNative.DirectoryInfo info, bool isFloat)
    {
        var pixels = (long)info.Width * info.Height;
        var packed = (pixels * info.BitsPerSample * info.SamplesPerPixel + 7) / 8;
        var converted = pixels * (isFloat ? 16 : info.SamplesPerPixel == 1 ? 1 : 4);

        return packed + converted;
    }

    /// <summary>
    /// Refuses a directory whose own layout cannot be read within the budget, before anything is
    /// allocated for it.
    /// </summary>
    internal static void RequireNativeLayoutFits(string path, TiffNative.DirectoryInfo info, bool isFloat)
    {
        if (info.Width == 0 || info.Height == 0 || info.Width > int.MaxValue || info.Height > int.MaxValue)
            throw new InvalidOperationException($"[TiffDecoder] {Path.GetFileName(path)} declares dimensions this path cannot read: {info.Width}x{info.Height}.");

        DecoderValidation.RequireSaneDimensions(nameof(TiffDecoder), (int)info.Width, (int)info.Height);

        var peak = NativeLayoutPeakBytes(info, isFloat);
        var budget = NativeLayoutBudgetBytes;

        if (peak <= budget)
            return;

        throw new InvalidOperationException($"[TiffDecoder] {Path.GetFileName(path)} is {info.Width}x{info.Height} at {info.BitsPerSample}-bit " +
                                            $"x{info.SamplesPerPixel}, which the RGBA interface refuses and which reading at its own layout would " +
                                            $"cost {peak / (1024 * 1024)} MB at peak, over the {budget / (1024 * 1024)} MB this machine allows. " +
                                            "Layouts read this way are held whole; nothing streams them.");
    }

    /// <summary>
    /// Decodes a directory whose sample layout libtiff's RGBA interface will not read.
    /// </summary>
    private static ICompositeContent DecodeUnsupportedLayout(string path, int directory,
        TiffNative.DirectoryInfo info, Composite composite, CancellationToken ct)
    {
        var isFloat = info.SampleFormat == 3;

        var kind = isFloat
            ? TiffNative.OutputKind.RgbaFloat
            : info.SamplesPerPixel == 1
                ? TiffNative.OutputKind.Gray8
                : TiffNative.OutputKind.Rgba8;

        RequireNativeLayoutFits(path, info, isFloat);

        Logger.Info($"[TiffDecoder] {Path.GetFileName(path)} is {info.BitsPerSample}-bit x{info.SamplesPerPixel} " +
                    $"{(isFloat ? "float" : "integer")}, which the RGBA interface refuses; " +
                    $"reading it at its own layout as {kind} " +
                    $"({NativeLayoutPeakBytes(info, isFloat) / (1024 * 1024)} MB at peak).");

        ct.ThrowIfCancellationRequested();

        if (!TiffNative.LoadNative(path, directory, kind, out var pixels, out var width, out var height, out var stride) || pixels == IntPtr.Zero)
        {
            var error = NativeErrors.GetUtf8ZOrAnsiZ(TiffNative.get_last_tiff_error());
            throw new InvalidOperationException($"[TiffDecoder] Failed to decode {path} at its own layout. {error}");
        }

        try
        {
            ct.ThrowIfCancellationRequested();

            DecoderValidation.RequireSaneDimensions(nameof(TiffDecoder), width, height);

            if (isFloat)
            {
                unsafe
                {
                    var floats = new Span<float>((void*)pixels, checked(width * height * 4));
                    var content = HdrImageBuilder.Build(floats, width, height, composite, ct, out var isGrayscale);

                    composite.AddFormatSpecific("GrayScale", isGrayscale.ToString());
                    return content;
                }
            }

            var colorType = kind == TiffNative.OutputKind.Gray8 ? SKColorType.Gray8 : SKColorType.Rgba8888;
            var alphaType = kind == TiffNative.OutputKind.Gray8 ? SKAlphaType.Opaque : SKAlphaType.Unpremul;

            var bitmap = new SKBitmap(new SKImageInfo(width, height, colorType, alphaType));

            if (kind == TiffNative.OutputKind.Gray8)
            {
                PixelCopy.CopyRows(pixels, stride, bitmap);
            }
            else
            {
                unsafe
                {
                    PixelCopy.CopyTightRgba(new ReadOnlySpan<byte>((void*)pixels, checked(width * height * 4)), bitmap);
                }
            }

            return RasterContentBuilder.Build(bitmap, composite);
        }
        finally
        {
            TiffNative.free_tiff_pixels(pixels);
        }
    }

    #endregion

    #region Native-depth gray

    /// <summary>
    /// Above this as RGBA8, a gray image is read at its own depth instead.
    /// </summary>
    private const long NativeDepthThresholdBytes = 256L * 1024 * 1024;

    /// <summary>
    /// Whether this directory is both able to take the gray path and large enough to want it.
    /// </summary>
    internal static bool WantsNativeDepth(TiffNative.DirectoryInfo info)
    {
        if (info.RegionCapable == 0 || info.Width == 0 || info.Height == 0)
            return false;

        return (long)info.Width * info.Height * 4 > NativeDepthThresholdBytes;
    }

    /// <summary>Whether a region of this directory comes back as RGBA rather than gray.</summary>
    private static bool IsColour(TiffNative.DirectoryInfo info) => info.RegionSamples == 4;

    /// <summary>
    /// How a region of this directory should be tagged. The native side resolves the fourth sample
    /// against EXTRASAMPLES - a sample that is not alpha comes back opaque - and says whether what
    /// does come back is associated, which is the one thing it cannot resolve without losing the
    /// values it would have to divide by.
    /// </summary>
    private static SKImageInfo RegionImageInfo(TiffNative.DirectoryInfo info, int width, int height) =>
        IsColour(info)
            ? new SKImageInfo(width, height, SKColorType.Rgba8888, info.RegionPremultiplied != 0 ? SKAlphaType.Premul : SKAlphaType.Unpremul)
            : new SKImageInfo(width, height, SKColorType.Gray8, SKAlphaType.Opaque);

    private const long StreamingThresholdBytes = 256L * 1024 * 1024;

    private const int StreamedTileEdge = 2048;

    private const long StreamedTileBudgetBytes = 64L * 1024 * 1024;

    /// <summary>Used when the directory's own layout gives nothing better to align to.</summary>
    private const uint DefaultPreviewBandRows = 256;

    /// <summary> What one band of the streaming pass may cost.</summary>
    private const long PreviewBandBudgetBytes = 192L * 1024 * 1024;

    /// <summary>How many rows the streaming pass asks for at a time.</summary>
    internal static uint PreviewBandRowsFor(TiffNative.DirectoryInfo info)
    {
        var bytesPerRow = Math.Max(1L, (long)info.Width * Math.Max((byte)1, info.RegionSamples));
        var budgetRows = (uint)Math.Clamp(PreviewBandBudgetBytes / bytesPerRow, 1, uint.MaxValue);

        var unit = info.IsTiled != 0 ? info.TileHeight : info.RowsPerStrip;

        // A layout unit that covers the whole sheet, or none at all, says nothing about alignment.
        if (unit == 0 || unit > info.Height)
            return Math.Min(DefaultPreviewBandRows, budgetRows);

        if (unit >= budgetRows)
            return budgetRows;

        return unit * (budgetRows / unit);
    }

    private static bool WantsStreaming(TiffNative.DirectoryInfo info) => (long)info.Width * info.Height * info.RegionSamples > StreamingThresholdBytes;

    /// <summary>
    /// Publishes a sheet too large to hold: a preview built in one streaming pass, and tiles
    /// decoded by region as the view asks for them.
    /// </summary>
    private static ICompositeContent StreamGray(string path, int directory, TiffNative.DirectoryInfo info,
        Composite composite, CancellationToken ct)
    {
        var width = (int)info.Width;
        var height = (int)info.Height;

        DecoderValidation.RequireSaneDimensions(nameof(TiffDecoder), width, height);

        Logger.Info($"[TiffDecoder] {Path.GetFileName(path)} is {width}x{height} at {info.BitsPerSample}-bit " +
                    $"{(IsColour(info) ? "colour" : "grey")} - " +
                    $"{(long)width * height * info.RegionSamples / (1024 * 1024)} MB if it were held whole. Streaming a preview and " +
                    "decoding tiles by region instead.");

        var content = new RasterLargeContent(width, height);

        composite.FullWidth = width;
        composite.FullHeight = height;

        try
        {
            var display = DecodeConstraintsProvider.Current;
            var maxEdge = display.LogicalWidth > 0 ? display.LogicalWidth : 2560;
            var maxHeight = display.LogicalHeight > 0 ? display.LogicalHeight : 2560;

            var colour = IsColour(info);
            var premultiplied = info.RegionPremultiplied != 0;
            var bandRows = PreviewBandRowsFor(info);

            var preview = StreamingGrayPreview.Build(
                width, height, maxEdge * 2, maxHeight * 2,
                (uint first, uint rows, out IntPtr pixels, out uint stride) => TiffNative.LoadRegion(path, directory, colour, 0, first, info.Width, rows, out pixels, out stride),
                TiffNative.free_tiff_pixels,
                bandRows,
                colour ? 4 : 1,
                ct,
                premultiplied
            );

            if (preview is not null)
                content.SetPreview(SKImage.FromBitmap(preview));

            var tilesX = (width + StreamedTileEdge - 1) / StreamedTileEdge;
            var tilesY = (height + StreamedTileEdge - 1) / StreamedTileEdge;

            var maxLevel = LevelsDownTo(width, height, preview?.Width ?? 0, preview?.Height ?? 0);

            var tiles = new LazyTileSource(
                tilesX, tilesY, StreamedTileEdge, StreamedTileEdge, 
                new RegionTileProvider(path, directory, width, height, colour, premultiplied, bandRows),
                StreamedTileBudgetBytes,
                bytesPerPixel: colour ? 4 : 1,
                maxLevel: maxLevel
            );

            content.SetTiles(tiles);
            content.MarkAllTilesReady(tilesX * tilesY);

            return content;
        }
        catch
        {
            content.Dispose();
            throw;
        }
    }

    /// <summary>
    /// How many halving it takes to bring the sheet down to roughly the preview's size.
    /// </summary>
    private static int LevelsDownTo(int width, int height, int previewWidth, int previewHeight)
    {
        if (previewWidth <= 0 || previewHeight <= 0)
            return 0;

        var ratio = Math.Max(width / (double)previewWidth, height / (double)previewHeight);
        if (ratio <= 1)
            return 0;

        return Math.Clamp((int)Math.Floor(Math.Log2(ratio)), 0, 8);
    }

    /// <summary>
    /// Decodes one tile straight out of the file.
    /// </summary>
    private sealed class RegionTileProvider(string path, int directory, int width, int height, bool colour, bool premultiplied, uint bandRows)
        : ITileProvider
    {
        private int Channels => colour ? 4 : 1;

        public SKImage? Decode(int level, int tileX, int tileY, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            var span = StreamedTileEdge << level;

            var x = (long)tileX * span;
            var y = (long)tileY * span;

            if (x >= width || y >= height)
                return null;

            var regionWidth = (uint)Math.Min(span, width - x);
            var regionHeight = (uint)Math.Min(span, height - y);

            var bitmap = level == 0
                ? DecodeDirect((uint)x, (uint)y, regionWidth, regionHeight)
                : DecodeReduced((uint)x, (uint)y, regionWidth, regionHeight, ct);

            return bitmap is null ? null : SKImage.FromBitmap(bitmap);
        }

        /// <summary>Full resolution: the region is the tile, so it is copied straight across.</summary>
        private SKBitmap? DecodeDirect(uint x, uint y, uint width, uint height)
        {
            if (!TiffNative.LoadRegion(path, directory, colour, x, y, width, height, out var pixels, out var stride)
                || pixels == IntPtr.Zero)
            {
                Warn(x, y);
                return null;
            }

            try
            {
                var info = colour
                    ? new SKImageInfo((int)width, (int)height, SKColorType.Rgba8888, premultiplied ? SKAlphaType.Premul : SKAlphaType.Unpremul)
                    : new SKImageInfo((int)width, (int)height, SKColorType.Gray8, SKAlphaType.Opaque);

                var bitmap = new SKBitmap(info);
                PixelCopy.CopyRows(pixels, stride, bitmap);
                bitmap.SetImmutable();

                return bitmap;
            }
            finally
            {
                TiffNative.free_tiff_pixels(pixels);
            }
        }

        /// <summary>
        /// A reduced level: the region is read a band at a time and averaged down, so a tile
        /// covering sixteen thousand pixels of sheet never exists at that size.
        /// </summary>
        private SKBitmap? DecodeReduced(uint x, uint y, uint width, uint height, CancellationToken ct)
        {
            return StreamingGrayPreview.Build(
                (int)width, (int)height, StreamedTileEdge, StreamedTileEdge,
                (uint first, uint rows, out IntPtr pixels, out uint stride) => TiffNative.LoadRegion(path, directory, colour, x, y + first, width, rows, out pixels, out stride),
                TiffNative.free_tiff_pixels,
                bandRows,
                Channels,
                ct,
                premultiplied
            );
        }

        private void Warn(uint x, uint y) =>
            Logger.Warning($"[TiffDecoder] Region {x},{y} of {Path.GetFileName(path)} failed: " + NativeErrors.GetUtf8ZOrAnsiZ(TiffNative.get_last_tiff_error()));

        public void Dispose() { }
    }

    /// <summary>
    /// Decodes a whole gray directory at its own bit depth into an 8-bit gray bitmap.
    /// </summary>
    private static SKBitmap DecodeGray(string path, int directory, TiffNative.DirectoryInfo info, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var width = (int)info.Width;
        var height = (int)info.Height;

        DecoderValidation.RequireSaneDimensions(nameof(TiffDecoder), width, height);

        Logger.Info($"[TiffDecoder] {Path.GetFileName(path)} is {width}x{height} at {info.BitsPerSample}-bit " +
                    $"{(IsColour(info) ? "colour" : "grey")}: " +
                    $"{(long)width * height * info.RegionSamples / (1024 * 1024)} MB read at its own depth, against " +
                    $"{(long)width * height * 4 / (1024 * 1024)} MB through the RGBA interface.");

        if (!TiffNative.LoadRegion(path, directory, IsColour(info), 0, 0, info.Width, info.Height, out var ptr, out var stride) || ptr == IntPtr.Zero)
        {
            var error = NativeErrors.GetUtf8ZOrAnsiZ(TiffNative.get_last_tiff_error());
            throw new InvalidOperationException($"[TiffDecoder] Failed to decode {path} at native depth. {error}");
        }

        try
        {
            ct.ThrowIfCancellationRequested();

            var bitmap = new SKBitmap(RegionImageInfo(info, width, height));

            PixelCopy.CopyRows(ptr, stride, bitmap);

            return bitmap;
        }
        finally
        {
            TiffNative.free_tiff_pixels(ptr);
        }
    }

    #endregion

    #region Multi-page document
    
    private const long PageResidentByteBudget = 256L * 1024 * 1024;

    /// <summary>
    /// Publishes the document as a set of pages, with the first decoded and the rest on demand.
    /// </summary>
    private void DecodeDocument(string path, Composite composite, IReadOnlyList<TiffNative.DirectoryInfo> directories, List<int> pages, CancellationToken ct)
    {
        Logger.Info($"[TiffDecoder] {Path.GetFileName(path)} holds {pages.Count} pages across {directories.Count} directories; decoding the first and the rest on demand.");

        composite.ExifInfo = MetadataProcessor.ParseMetadata(path);
        composite.Structure = [TiffPageSet.Summarise(directories, pages)];
        TiffPageSet.Describe(composite, directories, pages, IsBigTiff(path));

        ct.ThrowIfCancellationRequested();

        var first = DecodePage(path, pages[0], directories[pages[0]], composite, ct);
        var variants = TiffPageSet.Describe(directories, pages);

        composite.Content = new VariantRasterContent(variants, active: 0, first, new PageProvider(path, pages, directories, composite), PageResidentByteBudget)
        {
            GroupLabel = "PAGES"
        };
    }

    /// <summary>Decodes one directory into displayable content.</summary>
    private static ICompositeContent DecodePage(string path, int directory, TiffNative.DirectoryInfo info,
        Composite composite, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (info.RgbaCapable == 0 && info.NativeCapable != 0)
            return DecodeUnsupportedLayout(path, directory, info, composite, ct);
        
        if (info.GrayCapable != 0 && info is { Width: > 0, Height: > 0 })
            return RasterContentBuilder.Build(DecodeGray(path, directory, info, ct), composite);

        if (!TiffNative.LoadDirectory(path, IntPtr.Zero, 0, directory, out var ptr, out var width, out var height, out var iccPtr, out var iccSize) || ptr == IntPtr.Zero)
        {
            if (info.NativeCapable != 0)
                return DecodeUnsupportedLayout(path, directory, info, composite, ct);

            var error = NativeErrors.GetUtf8ZOrAnsiZ(TiffNative.get_last_tiff_error());
            throw new InvalidOperationException($"[TiffDecoder] Failed to decode page {directory} of {path}. {error}");
        }

        var bitmap = BuildBitmap(ptr, width, height, iccPtr, iccSize, tagColorSpace: true, ct);
        return RasterContentBuilder.Build(bitmap, composite);
    }

    /// <summary>
    /// Supplies pages as the interface asks for them. Holds the path rather than any open handle:
    /// a <c>TIFF*</c> is not thread-safe, and this is called from a background thread each time.
    /// </summary>
    private sealed class PageProvider(string path, List<int> pages, IReadOnlyList<TiffNative.DirectoryInfo> directories, Composite composite)
        : IVariantProvider
    {
        public ICompositeContent Decode(int index, CancellationToken ct) => DecodePage(path, pages[index], directories[pages[index]], composite, ct);
    }

    #endregion

    public SKBitmap? DecodeThumbnail(string path, int maxDimension, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // libtiff has no native scaled decode, so decode full then downscale.
        return ThumbnailScaler.ResizeToThumbnail(LoadBitmap(path, composite: null, ct, tagColorSpace: false, out _), maxDimension);
    }

    private static bool TryDecodeNative(string path, Composite? composite, CancellationToken ct, out IntPtr ptr, out int width, out int height, out IntPtr icc, out int iccSize, out bool metadataParsed)
    {
        metadataParsed = false;

        if (composite is not null && TiffNative.MemoryLoadAvailable && NativeFileBuffer.ShouldBuffer(composite.FileSizeBytes ?? 0))
        {
            using var data = NativeFileBuffer.Read(path, ct, out var readMs, composite.ReportTransferred);
            composite.CompleteTransfer((long)data.Length, readMs);
            ct.ThrowIfCancellationRequested();

            using (var metadata = data.AsStream())
            {
                composite.ExifInfo = MetadataProcessor.ParseMetadata(metadata, path);
            }

            metadataParsed = true;

            if (TiffNative.LoadFromMemory(data.Data, data.Length, out ptr, out width, out height, out icc, out iccSize))
                return ptr != IntPtr.Zero;

            if (TiffNative.MemoryLoadAvailable)
                return false;

            Logger.Warning("[TiffDecoder] Native library has no memory entry point; falling back to path decode.");
        }

        return TiffNative.load_tiff_rgba(path, out ptr, out width, out height, out icc, out iccSize) && ptr != IntPtr.Zero;
    }

    private static SKBitmap LoadBitmap(string path, Composite? composite, CancellationToken ct, bool tagColorSpace, out bool metadataParsed)
    {
        if (TryDecodeNative(path, composite, ct, out var ptr, out var width, out var height, out var iccPtr, out var iccSize, out metadataParsed))
            return BuildBitmap(ptr, width, height, iccPtr, iccSize, tagColorSpace, ct);

        var error = NativeErrors.GetUtf8ZOrAnsiZ(TiffNative.get_last_tiff_error());
        throw new InvalidOperationException($"[TiffDecoder] Failed to decode TIFF: {path}. {error}");
    }

    private static SKBitmap BuildBitmap(IntPtr ptr, int width, int height, IntPtr iccPtr, int iccSize, bool tagColorSpace, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();

            DecoderValidation.RequireSaneDimensions(nameof(TiffDecoder), width, height);

            var colorSpace = tagColorSpace ? ResolveIccColorSpace(iccPtr, iccSize) : null;

            var byteCount = checked(width * height * 4);
            var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul, colorSpace);
            var bitmap = new SKBitmap(info);

            unsafe
            {
                PixelCopy.CopyTightRgba(new ReadOnlySpan<byte>((void*)ptr, byteCount), bitmap);
            }

            return bitmap;
        }
        finally
        {
            TiffNative.free_tiff_pixels(ptr);
            if (iccPtr != IntPtr.Zero)
                TiffNative.free_tiff_pixels(iccPtr);
        }
    }

    private static SKColorSpace? ResolveIccColorSpace(IntPtr iccPtr, int iccSize)
    {
        if (iccPtr == IntPtr.Zero || iccSize <= 0)
            return null;

        try
        {
            var icc = new byte[iccSize];
            Marshal.Copy(iccPtr, icc, 0, iccSize);
            return SKColorSpace.CreateIcc(icc);
        }
        catch (Exception ex)
        {
            Logger.Debug($"[TiffDecoder] ICC profile parse failed: {ex.Message}");
            return null;
        }
    }
}