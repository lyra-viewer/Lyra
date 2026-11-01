using Lyra.Common;
using Lyra.Imaging.Content.Status;
using Lyra.Imaging.Decoding.Support;
using Lyra.Imaging.Interop;
using Lyra.Imaging.Metadata;
using SkiaSharp;

namespace Lyra.Imaging.Decoding.Decoders.Tiff;

/// <summary>
/// Reads through libtiff's RGBA interface: one 8-bit RGBA raster, premultiplied, top-left origin.
/// libtiff converts unassociated alpha itself, so Premul holds for both EXTRASAMPLES variants.
/// </summary>
internal static class TiffWholeImage
{
    /// <summary>Checked before reading, since libtiff would decode everything before the copy fails.</summary>
    internal static void RequireWithinOneBitmap(string path, TiffNative.DirectoryInfo info)
    {
        var bytes = (long)info.Width * info.Height * 4;
        if (bytes <= int.MaxValue)
            return;

        throw new LoadFailureException(LoadFailureKind.TooLarge,
            $"{Path.GetFileName(path)} is {info.Width}x{info.Height}, " +
            $"{bytes / (1024 * 1024)} MB as RGBA: more than one bitmap holds, in a layout " +
            "that cannot be read by region.");
    }

    /// <summary>
    /// With a <paramref name="context"/>, the file is fetched whole first - into memory, or to
    /// local scratch when too large - so libtiff's small reads land on a local copy.
    /// </summary>
    public static TiffRead Read(string path, DecodeContext? context, CancellationToken ct, out bool metadataParsed)
    {
        metadataParsed = false;

        var fileSize = context?.Source.Length ?? 0;

        if (context is not null && TiffNative.MemoryLoadAvailable && NativeFileBuffer.ShouldBuffer(fileSize))
        {
            using var data = TiffFetch.IntoMemory(path, context, ct)!;

            metadataParsed = true;

            var fromMemory = TiffReads.FromMemory(data.Data, data.Length);

            // Falls through to the path only when the memory entry point is missing.
            if (fromMemory.HasPixels || TiffNative.MemoryLoadAvailable)
                return fromMemory;

            Logger.Warning("[TiffDecoder] Native library has no memory entry point; falling back to path decode.");
        }
        else if (context is not null && fileSize > 0 && !NativeFileBuffer.ShouldBuffer(fileSize))
        {
            using var scratch = ScratchFileCopy.TryCreate(path, fileSize, ct, out var copyMs, context.ReportTransferred);
            if (scratch is not null)
            {
                context.CompleteTransfer(scratch.BytesCopied, copyMs);
                ct.ThrowIfCancellationRequested();

                context.ExifInfo = MetadataProcessor.ParseMetadata(scratch.Path);
                metadataParsed = true;

                return TiffReads.LocalWholeImage(scratch.Path);
            }
        }

        var transfer = new IoTally(context);
        var read = TiffReads.WholeImage(path, fileSize > 0 ? fileSize : TiffReads.SafeFileLength(path), ct, transfer);

        transfer.ReportTo(context);

        return read;
    }

    /// <summary>Reads in place: fetching a whole document to show one page would copy every page.</summary>
    public static TiffRead ReadDirectory(string path, int directory, TiffNative.DirectoryInfo info, CancellationToken ct) =>
        TiffReads.Directory(path, directory, (long)info.Width * info.Height * 4, ct);

    /// <summary>Always frees the read, whether or not the copy succeeds.</summary>
    public static SKBitmap ToBitmap(TiffRead read, bool tagColorSpace, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();

            DecoderValidation.RequireSaneDimensions(read.Width, read.Height);

            var colorSpace = tagColorSpace ? IccColorSpace.FromNative(read.Icc, read.IccSize, nameof(TiffDecoder)) : null;

            var byteCount = checked(read.Width * read.Height * 4);
            var info = new SKImageInfo(read.Width, read.Height, SKColorType.Rgba8888, SKAlphaType.Premul, colorSpace);
            var bitmap = new SKBitmap(info);

            unsafe
            {
                PixelCopy.CopyTightRgba(new ReadOnlySpan<byte>((void*)read.Pixels, byteCount), bitmap);
            }

            return bitmap;
        }
        finally
        {
            TiffNative.free_tiff_pixels(read.Pixels);

            if (read.Icc != IntPtr.Zero)
                TiffNative.free_tiff_pixels(read.Icc);
        }
    }
}
