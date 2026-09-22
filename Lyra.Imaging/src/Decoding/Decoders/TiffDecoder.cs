using Lyra.Common;
using Lyra.Imaging.Content.Tiling;
using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Structure;
using Lyra.Imaging.Decoding.Support;
using Lyra.Imaging.Interop;
using Lyra.Imaging.Loading;
using SkiaSharp;
using Lyra.Imaging.Metadata;

namespace Lyra.Imaging.Decoding.Decoders;

/// <summary>
/// Decodes TIFF images via the native libtiff wrapper. libtiff handles every compression,
/// photometric, planar and tiled variant and returns an 8-bit RGBA raster with
/// <em>premultiplied</em> (associated) alpha, top-left origin. Unassociated-alpha TIFFs are
/// converted by libtiff itself (the UaToAa table in tif_getimage.c), so Premul is correct
/// for both EXTRASAMPLES variants.
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
            DecodeDocument(path, composite, directories, encodedBytes, pages, ct);
            return;
        }

        var page = pages.Count > 0 ? pages[0] : 0;
        var info = directories.Count > page ? directories[page] : default;

        TiffPageSet.Describe(composite, directories, pages, BigTiffMetadataReader.IsBigTiff(path));

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
                : RasterContentBuilder.Build(DecodeGray(path, page, info, composite, ct), composite);
        }
        else
        {
            try
            {
                RequireRgbaWithinOneBitmap(path, info);

                var bitmap = LoadBitmap(path, composite, ct, tagColorSpace: true, out var metadataParsed);
                if (!metadataParsed)
                    composite.ExifInfo = MetadataProcessor.ParseMetadata(path);

                composite.Content = RasterContentBuilder.Build(bitmap, composite);
            }
            catch (InvalidOperationException) when (info.NativeCapable != 0)
            {
                Logger.Debug($"[TiffDecoder] The RGBA interface accepted {Path.GetFileName(path)} and then refused it; reading it at its own layout instead.");

                if (composite.ExifInfo is null)
                    composite.ExifInfo = MetadataProcessor.ParseMetadata(path);

                composite.Content = DecodeUnsupportedLayout(path, page, info, composite, ct);
            }
        }

        if (directories.Count > 1)
            composite.Structure = [TiffPageSet.Summarise(directories, pages)];
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
    internal static void RequireNativeLayoutWithinBudget(string path, TiffNative.DirectoryInfo info, bool isFloat)
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

    /// <summary>Decodes a directory whose sample layout libtiff's RGBA interface will not read.</summary>
    private static ICompositeContent DecodeUnsupportedLayout(string path, int directory,
        TiffNative.DirectoryInfo info, Composite composite, CancellationToken ct)
    {
        var isFloat = info.SampleFormat == 3;

        var kind = isFloat
            ? TiffNative.OutputKind.RgbaFloat
            : info.SamplesPerPixel == 1
                ? TiffNative.OutputKind.Gray8
                : TiffNative.OutputKind.Rgba8;

        RequireNativeLayoutWithinBudget(path, info, isFloat);

        Logger.Info($"[TiffDecoder] {Path.GetFileName(path)} is {info.BitsPerSample}-bit x{info.SamplesPerPixel} " +
                    $"{(isFloat ? "float" : "integer")}, which the RGBA interface refuses; " +
                    $"reading it at its own layout as {kind} " +
                    $"({NativeLayoutPeakBytes(info, isFloat) / (1024 * 1024)} MB at peak).");

        ct.ThrowIfCancellationRequested();

        int width = 0, height = 0;
        uint stride = 0;

        var transfer = new IoTally();
        string? failure = null;
        
        var loaded = BoundedNativeRead.Run(
            $"Native layout of {Path.GetFileName(path)}",
            NativeLayoutPeakBytes(info, isFloat),
            (out IntPtr buffer) =>
            {
                var read = TiffNative.LoadNative(path, directory, kind, out buffer, out width, out height, out stride);

                if (!read)
                    failure = NativeError();

                transfer.AddLastRead();

                return read;
            },
            out var pixels, out var timedOut, TiffNative.free_tiff_pixels
        );

        transfer.ReportTo(composite);

        if (!loaded || pixels == IntPtr.Zero)
        {
            var error = timedOut
                ? "The read did not return in time; the file may be on a source that has stopped responding."
                : failure ?? string.Empty;

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
    /// Whether this directory is both able to take the gray path and large enough to want it.
    /// </summary>
    internal static bool WantsNativeDepth(TiffNative.DirectoryInfo info)
    {
        if (info.RegionCapable == 0 || info.Width == 0 || info.Height == 0)
            return false;

        return (long)info.Width * info.Height * 4 > DecodePolicy.RgbaFormCeilingBytes;
    }

    /// <summary>Whether a region of this directory comes back as RGBA rather than gray.</summary>
    private static bool IsColour(TiffNative.DirectoryInfo info) => info.RegionSamples == 4;

    /// <summary>
    /// What one region read produced. A record rather than a handful of out parameters because
    /// most callers want two of these four and have to spell out discards for the rest, which at
    /// the call site reads as nothing at all.
    /// </summary>
    /// <param name="Ok">Whether pixels came back.</param>
    /// <param name="TimedOut">
    /// Whether it failed by running out of time rather than by being unreadable - what tells a
    /// caller reading many regions to stop rather than spend the whole bound on every one.
    /// </param>
    /// <param name="Error">Why libtiff refused, empty unless <paramref name="Ok"/> is false.</param>
    private readonly record struct RegionRead(bool Ok, IntPtr Pixels, uint Stride, bool TimedOut, string Error)
    {
        public bool HasPixels => Ok && Pixels != IntPtr.Zero;
    }
    
    private sealed class IoTally
    {
        private long _bytes;
        private long _microseconds;

        public void Add(long bytes, double ms)
        {
            Interlocked.Add(ref _bytes, bytes);
            Interlocked.Add(ref _microseconds, (long)(ms * 1000));
        }

        public void AddLastRead()
        {
            if (TiffNative.LastIo() is { } io)
                Add(io.Bytes, io.Ms);
        }

        public void ReportTo(Composite? composite)
        {
            var bytes = Interlocked.Read(ref _bytes);
            if (composite is null || bytes <= 0)
                return;

            composite.CompleteTransfer(bytes, Interlocked.Read(ref _microseconds) / 1000.0);
        }
    }
    
    private static RegionRead BoundedLoadRegion(string path, int directory, bool colour, uint x, uint y, uint width, uint height, string context, IoTally? tally = null)
    {
        uint stride = 0;
        string? failure = null;

        var ok = BoundedNativeRead.Run(context, (long)width * height * (colour ? 4 : 1),
            (out IntPtr buffer) =>
            {
                var read = TiffNative.LoadRegion(path, directory, colour, x, y, width, height, out buffer, out stride);

                if (!read)
                    failure = NativeError();

                tally?.AddLastRead();

                return read;
            },
            out var pixels, out var timedOut, TiffNative.free_tiff_pixels);

        return new RegionRead(ok, pixels, stride, timedOut, failure ?? string.Empty);
    }
    
    /// <summary>Reads full-width bands of a directory, for a streaming pass over the whole sheet.</summary>
    private static StreamingGrayPreview.BandReader FullWidthBands(string path, int directory, TiffNative.DirectoryInfo info, string purpose, IoTally? tally = null) =>
        (uint first, uint rows, out IntPtr pixels, out uint stride) =>
        {
            var read = BoundedLoadRegion(path, directory, IsColour(info), 0, first, info.Width, rows, $"{purpose} band {first}..{first + rows} of {Path.GetFileName(path)}", tally);

            pixels = read.Pixels;
            stride = read.Stride;

            return read.Ok;
        };

    private static string NativeError() => NativeErrors.GetUtf8ZOrAnsiZ(TiffNative.get_last_tiff_error());

    /// <summary>
    /// Refuses a directory the RGBA interface cannot deliver: its output is one buffer copied into
    /// one bitmap, so past an int of bytes it fails in <see cref="BuildBitmap"/> - after libtiff
    /// has already allocated and decoded all of it.
    /// </summary>
    internal static void RequireRgbaWithinOneBitmap(string path, TiffNative.DirectoryInfo info)
    {
        var bytes = (long)info.Width * info.Height * 4;
        if (bytes <= int.MaxValue)
            return;

        throw new InvalidOperationException($"[TiffDecoder] {Path.GetFileName(path)} is {info.Width}x{info.Height}, " +
                                            $"{bytes / (1024 * 1024)} MB as RGBA: more than one bitmap holds, in a layout " +
                                            "that cannot be read by region.");
    }

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

    /// <summary>Used when the directory's own layout gives nothing better to align to.</summary>
    private const uint DefaultPreviewBandRows = 256;

    /// <summary>How many rows the streaming pass asks for at a time.</summary>
    internal static uint PreviewBandRowsFor(TiffNative.DirectoryInfo info)
    {
        var bytesPerRow = Math.Max(1L, (long)info.Width * Math.Max((byte)1, info.RegionSamples));
        var budgetRows = (uint)Math.Clamp(DecodePolicy.PreviewBandBudgetBytes / bytesPerRow, 1, uint.MaxValue);

        var unit = info.IsTiled != 0 ? info.TileHeight : info.RowsPerStrip;

        // A layout unit that covers the whole sheet, or none at all, says nothing about alignment.
        if (unit == 0 || unit > info.Height)
            return Math.Min(DefaultPreviewBandRows, budgetRows);

        if (unit >= budgetRows)
            return budgetRows;

        return unit * (budgetRows / unit);
    }

    private static bool WantsStreaming(TiffNative.DirectoryInfo info) => (long)info.Width * info.Height * info.RegionSamples > DecodePolicy.SingleTextureCeilingBytes;

    /// <summary>
    /// Publishes a sheet too large to hold: a preview built in one streaming pass, and tiles
    /// decoded by region as the view asks for them.
    /// </summary>
    private static ICompositeContent StreamGray(string path, int directory, TiffNative.DirectoryInfo info, Composite composite, CancellationToken ct)
    {
        var width = (int)info.Width;
        var height = (int)info.Height;

        DecoderValidation.RequireSaneDimensions(nameof(TiffDecoder), width, height);

        Logger.Debug($"[TiffDecoder] {Path.GetFileName(path)} is {width}x{height} at {info.BitsPerSample}-bit " +
                     $"{(IsColour(info) ? "colour" : "grey")} - " +
                     $"{(long)width * height * info.RegionSamples / (1024 * 1024)} MB if it were held whole. Streaming a preview and " +
                     "decoding tiles by region instead.");

        var content = new RasterLargeContent(width, height);

        composite.FullWidth = width;
        composite.FullHeight = height;

        try
        {
            var (maxWidth, maxHeight) = RasterContentBuilder.PreviewBounds();

            var colour = IsColour(info);
            var premultiplied = info.RegionPremultiplied != 0;
            var bandRows = PreviewBandRowsFor(info);
            
            var transfer = new IoTally();

            var preview = StreamingGrayPreview.Build(
                width, height, maxWidth, maxHeight,
                FullWidthBands(path, directory, info, "Preview", transfer),
                TiffNative.free_tiff_pixels,
                bandRows,
                colour ? 4 : 1,
                ct,
                premultiplied
            );

            transfer.ReportTo(composite);

            if (preview is not null)
                content.SetPreview(SKImage.FromBitmap(preview));

            var tilesX = (width + DecodePolicy.TileEdge - 1) / DecodePolicy.TileEdge;
            var tilesY = (height + DecodePolicy.TileEdge - 1) / DecodePolicy.TileEdge;

            var maxLevel = LevelsDownTo(width, height, preview?.Width ?? 0, preview?.Height ?? 0);

            var tiles = new LazyTileSource(
                tilesX, tilesY, DecodePolicy.TileEdge, DecodePolicy.TileEdge, 
                new RegionTileProvider(path, directory, width, height, colour, premultiplied, bandRows),
                DecodePolicy.ResidentTileBudgetBytes,
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

        /// <summary>How many reads must time out in a row before this file is given up on.</summary>
        private const int TimeoutsBeforeGivingUp = 3;

        private int _consecutiveTimeouts;

        /// <summary>
        /// Set once reads have timed out often enough in a row to call the source wedged rather
        /// than slow. Every later tile would spend the whole bound finding that out again, so the
        /// image stops asking and keeps the preview it already has. Reopening it starts a new
        /// provider, and a new chance.
        /// </summary>
        private int _stalled;

        private void RecordTimeout()
        {
            if (Interlocked.Increment(ref _consecutiveTimeouts) < TimeoutsBeforeGivingUp)
                return;

            if (Interlocked.Exchange(ref _stalled, 1) == 0)
                Logger.Warning($"[TiffDecoder] {TimeoutsBeforeGivingUp} reads of {Path.GetFileName(path)} in a row did not " +
                               "return in time; no further tiles will be requested for it. The preview stays on screen.");
        }

        /// <summary>A read that lands says the source is alive, whatever the ones before it did.</summary>
        private void RecordSuccess() => Interlocked.Exchange(ref _consecutiveTimeouts, 0);

        public SKImage? Decode(int level, int tileX, int tileY, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            if (Volatile.Read(ref _stalled) != 0)
                return null;

            var span = DecodePolicy.TileEdge << level;

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
            var read = BoundedLoadRegion(path, directory, colour, x, y, width, height,
                $"Tile {x},{y} of {Path.GetFileName(path)}");

            if (!read.HasPixels)
            {
                if (read.TimedOut)
                    RecordTimeout();
                else
                    Warn(x, y, read.Error);

                return null;
            }

            RecordSuccess();

            try
            {
                var info = colour
                    ? new SKImageInfo((int)width, (int)height, SKColorType.Rgba8888, premultiplied ? SKAlphaType.Premul : SKAlphaType.Unpremul)
                    : new SKImageInfo((int)width, (int)height, SKColorType.Gray8, SKAlphaType.Opaque);

                var bitmap = new SKBitmap(info);
                PixelCopy.CopyRows(read.Pixels, read.Stride, bitmap);
                bitmap.SetImmutable();

                return bitmap;
            }
            finally
            {
                TiffNative.free_tiff_pixels(read.Pixels);
            }
        }

        /// <summary>
        /// A reduced level: the region is read a band at a time and averaged down, so a tile
        /// covering sixteen thousand pixels of sheet never exists at that size.
        /// </summary>
        private SKBitmap? DecodeReduced(uint x, uint y, uint width, uint height, CancellationToken ct)
        {
            bool ReadBand(uint first, uint rows, out IntPtr pixels, out uint stride)
            {
                var read = BoundedLoadRegion(path, directory, colour, x, y + first, width, rows,
                    $"Reduced band {first}..{first + rows} of tile {x},{y} in {Path.GetFileName(path)}");

                pixels = read.Pixels;
                stride = read.Stride;

                if (read.Ok)
                    RecordSuccess();
                else if (read.TimedOut)
                    RecordTimeout();
                else
                    Warn(x, y + first, read.Error);

                return read.Ok;
            }

            return StreamingGrayPreview.Build(
                (int)width, (int)height, DecodePolicy.TileEdge, DecodePolicy.TileEdge,
                ReadBand,
                TiffNative.free_tiff_pixels,
                bandRows,
                Channels,
                ct,
                premultiplied
            );
        }

        private void Warn(uint x, uint y, string nativeError) =>
            Logger.Warning($"[TiffDecoder] Region {x},{y} of {Path.GetFileName(path)} failed: {nativeError}");

        public void Dispose() { }
    }

    /// <summary>
    /// Decodes a whole gray directory at its own bit depth into an 8-bit gray bitmap.
    /// </summary>
    private static SKBitmap DecodeGray(string path, int directory, TiffNative.DirectoryInfo info, Composite composite, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var width = (int)info.Width;
        var height = (int)info.Height;

        DecoderValidation.RequireSaneDimensions(nameof(TiffDecoder), width, height);

        Logger.Info($"[TiffDecoder] {Path.GetFileName(path)} is {width}x{height} at {info.BitsPerSample}-bit " +
                    $"{(IsColour(info) ? "colour" : "grey")}: " +
                    $"{(long)width * height * info.RegionSamples / (1024 * 1024)} MB read at its own depth, against " +
                    $"{(long)width * height * 4 / (1024 * 1024)} MB through the RGBA interface.");

        var transfer = new IoTally();

        var read = BoundedLoadRegion(path, directory, IsColour(info), 0, 0, info.Width, info.Height, $"Full region of {Path.GetFileName(path)}", transfer);

        transfer.ReportTo(composite);

        if (!read.HasPixels)
        {
            var error = read.TimedOut
                ? "The read did not return in time; the file may be on a source that has stopped responding."
                : read.Error;

            throw new InvalidOperationException($"[TiffDecoder] Failed to decode {path} at native depth. {error}");
        }

        try
        {
            ct.ThrowIfCancellationRequested();

            var bitmap = new SKBitmap(RegionImageInfo(info, width, height));

            PixelCopy.CopyRows(read.Pixels, read.Stride, bitmap);

            return bitmap;
        }
        finally
        {
            TiffNative.free_tiff_pixels(read.Pixels);
        }
    }

    #endregion

    #region Multi-page document
    
    /// <summary>
    /// Publishes the document as a set of pages, with the first decoded and the rest on demand.
    /// </summary>
    private void DecodeDocument(string path, Composite composite, IReadOnlyList<TiffNative.DirectoryInfo> directories, long[]? encodedBytes, List<int> pages, CancellationToken ct)
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

    /// <summary>Decodes one directory into displayable content.</summary>
    private static ICompositeContent DecodePage(string path, int directory, TiffNative.DirectoryInfo info,
        Composite composite, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (info.RgbaCapable == 0 && info.NativeCapable != 0)
            return DecodeUnsupportedLayout(path, directory, info, composite, ct);

        // As for a single-page file: a page too large to hold streams rather than being read whole.
        if (WantsNativeDepth(info) && WantsStreaming(info))
            return StreamGray(path, directory, info, composite, ct);

        if (info.GrayCapable != 0 && info is { Width: > 0, Height: > 0 })
            return RasterContentBuilder.Build(DecodeGray(path, directory, info, composite, ct), composite);

        if ((long)info.Width * info.Height * 4 > int.MaxValue && info.NativeCapable != 0)
            return DecodeUnsupportedLayout(path, directory, info, composite, ct);

        RequireRgbaWithinOneBitmap(path, info);

        IntPtr readIcc = IntPtr.Zero;
        int readWidth = 0, readHeight = 0, readIccSize = 0;
        string? failure = null;
        
        var loaded = BoundedNativeRead.Run(
            $"Page {directory} of {Path.GetFileName(path)}",
            (long)info.Width * info.Height * 4,
            (out IntPtr buffer) =>
            {
                var read = TiffNative.LoadDirectory(path, IntPtr.Zero, 0, directory, out buffer, out readWidth, out readHeight, out readIcc, out readIccSize);

                if (!read)
                    failure = NativeError();

                return read;
            },
            out var ptr, out var timedOut,
            late =>
            {
                TiffNative.free_tiff_pixels(late);

                if (readIcc != IntPtr.Zero)
                    TiffNative.free_tiff_pixels(readIcc);
            }
        );

        var (width, height, iccPtr, iccSize) = (readWidth, readHeight, readIcc, readIccSize);

        if (!loaded || ptr == IntPtr.Zero)
        {
            if (info.NativeCapable != 0 && !timedOut)
                return DecodeUnsupportedLayout(path, directory, info, composite, ct);

            var error = timedOut
                ? "The read did not return in time; the file may be on a source that has stopped responding."
                : failure ?? string.Empty;

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

        var directories = TiffNative.DescribeDirectories(path, IntPtr.Zero, 0);
        var pages = TiffPageSet.Pages(directories);
        var page = pages.Count > 0 ? pages[0] : 0;

        if (directories.Count > page)
        {
            var info = directories[page];
            if (WantsNativeDepth(info))
                return StreamThumbnail(path, page, info, maxDimension, ct);

            RequireRgbaWithinOneBitmap(path, info);
        }

        // libtiff has no native scaled decode, so decode full then downscale.
        return ThumbnailScaler.ResizeToThumbnail(LoadBitmap(path, composite: null, ct, tagColorSpace: false, out _), maxDimension);
    }

    private static SKBitmap? StreamThumbnail(string path, int directory, TiffNative.DirectoryInfo info, int maxDimension, CancellationToken ct)
    {
        var width = (int)info.Width;
        var height = (int)info.Height;

        DecoderValidation.RequireSaneDimensions(nameof(TiffDecoder), width, height);

        return StreamingGrayPreview.Build(
            width, height, maxDimension, maxDimension,
            FullWidthBands(path, directory, info, "Thumbnail"),
            TiffNative.free_tiff_pixels,
            PreviewBandRowsFor(info),
            IsColour(info) ? 4 : 1,
            ct,
            info.RegionPremultiplied != 0
        );
    }

    private static bool TryDecodeNative(string path, Composite? composite, CancellationToken ct, out IntPtr ptr, out int width, out int height, out IntPtr icc, out int iccSize, out bool metadataParsed, out string nativeError)
    {
        metadataParsed = false;
        nativeError = string.Empty;

        var fileSize = composite?.FileSizeBytes ?? 0;

        if (composite is not null && TiffNative.MemoryLoadAvailable && NativeFileBuffer.ShouldBuffer(fileSize))
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
            {
                nativeError = NativeError();
                return false;
            }

            Logger.Warning("[TiffDecoder] Native library has no memory entry point; falling back to path decode.");
        }
        else if (composite is not null && fileSize > 0 && !NativeFileBuffer.ShouldBuffer(fileSize))
        {
            using var scratch = ScratchFileCopy.TryCreate(path, fileSize, ct, out var copyMs, composite.ReportTransferred);
            if (scratch is not null)
            {
                composite.CompleteTransfer(scratch.BytesCopied, copyMs);
                ct.ThrowIfCancellationRequested();

                composite.ExifInfo = MetadataProcessor.ParseMetadata(scratch.Path);
                metadataParsed = true;

                return TiffNative.load_tiff_rgba(scratch.Path, out ptr, out width, out height, out icc, out iccSize) && ptr != IntPtr.Zero;
            }
        }
        
        IntPtr readIcc = IntPtr.Zero;
        int readWidth = 0, readHeight = 0, readIccSize = 0;

        var transfer = new IoTally();
        string? failure = null;

        var loaded = BoundedNativeRead.Run(
            $"Whole image of {Path.GetFileName(path)}",
            fileSize > 0 ? fileSize : SafeFileLength(path),
            (out IntPtr buffer) =>
            {
                var read = TiffNative.load_tiff_rgba(path, out buffer, out readWidth, out readHeight, out readIcc,
                    out readIccSize);

                if (!read)
                    failure = NativeError();

                transfer.AddLastRead();

                return read;
            },
            out ptr, out var timedOut,
            late =>
            {
                TiffNative.free_tiff_pixels(late);

                if (readIcc != IntPtr.Zero)
                    TiffNative.free_tiff_pixels(readIcc);
            }
        );

        width = readWidth;
        height = readHeight;
        icc = readIcc;
        iccSize = readIccSize;

        transfer.ReportTo(composite);

        nativeError = timedOut
            ? "The read did not return in time; the file may be on a source that has stopped responding."
            : failure ?? string.Empty;

        return loaded && ptr != IntPtr.Zero;
    }

    /// <summary>The file's size for sizing a read's time bound, or 0 when it cannot be had.</summary>
    private static long SafeFileLength(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (Exception ex)
        {
            Logger.Debug($"[TiffDecoder] Could not size {path} for its read bound: {ex.Message}");
            return 0;
        }
    }

    private static SKBitmap LoadBitmap(string path, Composite? composite, CancellationToken ct, bool tagColorSpace, out bool metadataParsed)
    {
        if (TryDecodeNative(path, composite, ct, out var ptr, out var width, out var height, out var iccPtr, out var iccSize, out metadataParsed, out var error))
            return BuildBitmap(ptr, width, height, iccPtr, iccSize, tagColorSpace, ct);

        throw new InvalidOperationException($"[TiffDecoder] Failed to decode TIFF: {path}. {error}");
    }

    private static SKBitmap BuildBitmap(IntPtr ptr, int width, int height, IntPtr iccPtr, int iccSize, bool tagColorSpace, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();

            DecoderValidation.RequireSaneDimensions(nameof(TiffDecoder), width, height);

            var colorSpace = tagColorSpace ? IccColorSpace.FromNative(iccPtr, iccSize, nameof(TiffDecoder)) : null;

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
}