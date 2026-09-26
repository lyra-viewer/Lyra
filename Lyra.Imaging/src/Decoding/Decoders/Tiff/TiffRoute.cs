using Lyra.Common;
using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Support;
using Lyra.Imaging.Interop;
using SkiaSharp;

namespace Lyra.Imaging.Decoding.Decoders.Tiff;

internal enum TiffReadPath
{
    WholeImage,
    Region,
    RegionStreamed,
    NativeLayout
}

/// <summary>Chooses how a directory is read, for a single image, a page and a thumbnail alike.</summary>
internal static class TiffRoute
{
    public static TiffReadPath For(TiffNative.DirectoryInfo info)
    {
        if (info.RgbaCapable == 0 && info.NativeCapable != 0)
            return TiffReadPath.NativeLayout;

        if (TiffRegion.WantsNativeDepth(info))
            return TiffRegion.WantsStreaming(info) ? TiffReadPath.RegionStreamed : TiffReadPath.Region;

        if (IsPlainGray(info))
            return TiffReadPath.Region;

        if (ExceedsOneRgbaBitmap(info) && info.NativeCapable != 0)
            return TiffReadPath.NativeLayout;

        return TiffReadPath.WholeImage;
    }

    /// <summary>
    /// Region reads copy rows as stored and ignore ICC profiles, so only gray without either
    /// concern takes that path by choice.
    /// </summary>
    private static bool IsPlainGray(TiffNative.DirectoryInfo info) =>
        info.GrayCapable != 0 && !info.HasIcc && info.Orientation == 1 && info is { Width: > 0, Height: > 0 };

    private static bool ExceedsOneRgbaBitmap(TiffNative.DirectoryInfo info) => (long)info.Width * info.Height * 4 > int.MaxValue;

    /// <summary>A timed-out source would only time out again.</summary>
    public static bool RetryAtNativeLayout(TiffNative.DirectoryInfo info, TiffRead failed) => info.NativeCapable != 0 && !failed.TimedOut;

    public static ICompositeContent Decode(string path, int directory, TiffNative.DirectoryInfo info, Composite composite, Func<TiffRead> readWhole, CancellationToken ct, Func<NativeFileBuffer?>? fetch = null)
    {
        ct.ThrowIfCancellationRequested();

        switch (For(info))
        {
            case TiffReadPath.NativeLayout:
                return TiffNativeLayout.Decode(path, directory, info, composite, ct);

            case TiffReadPath.RegionStreamed:
                return TiffRegion.Stream(path, directory, info, composite, ct);

            case TiffReadPath.Region:
            {
                using var fetched = TiffNative.MemoryRegionAvailable ? fetch?.Invoke() : null;
                return RasterContentBuilder.Build(TiffRegion.DecodeWhole(path, directory, info, composite, ct, fetched), composite);
            }
        }

        TiffWholeImage.RequireWithinOneBitmap(path, info);

        var read = readWhole();

        if (read.HasPixels)
            return RasterContentBuilder.Build(TiffWholeImage.ToBitmap(read, tagColorSpace: true, ct), composite);

        if (!RetryAtNativeLayout(info, read))
            throw new InvalidOperationException($"[TiffDecoder] Failed to decode directory {directory} of {path}. {read.Reason}");

        Logger.Debug($"[TiffDecoder] The RGBA interface accepted directory {directory} of {Path.GetFileName(path)} and then " +
                     $"refused it ({read.Reason}); reading it at its own layout instead.");

        return TiffNativeLayout.Decode(path, directory, info, composite, ct);
    }

    public static SKBitmap? Thumbnail(string path, int directory, TiffNative.DirectoryInfo info, int maxDimension,
        Func<TiffRead> readWhole, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        switch (For(info))
        {
            case TiffReadPath.NativeLayout:
                return TiffNativeLayout.Thumbnail(path, directory, info, maxDimension, ct);

            case TiffReadPath.Region:
            case TiffReadPath.RegionStreamed:
                return TiffRegion.StreamThumbnail(path, directory, info, maxDimension, ct);
        }

        TiffWholeImage.RequireWithinOneBitmap(path, info);

        var read = readWhole();

        if (read.HasPixels)
            return ThumbnailScaler.ResizeToThumbnail(TiffWholeImage.ToBitmap(read, tagColorSpace: false, ct), maxDimension);

        if (!RetryAtNativeLayout(info, read))
            throw new InvalidOperationException($"[TiffDecoder] Failed to thumbnail directory {directory} of {path}. {read.Reason}");

        return TiffNativeLayout.Thumbnail(path, directory, info, maxDimension, ct);
    }
}
