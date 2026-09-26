using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Support;
using Lyra.Imaging.Metadata;

namespace Lyra.Imaging.Decoding.Decoders.Tiff;

/// <summary>
/// Fetches an image's file whole before libtiff reads it: one sequential read the load can
/// measure and report as it goes, and a copy for libtiff's many small reads to land on instead of
/// a slow source. Metadata is read from the same copy, sparing a second trip.
/// </summary>
internal static class TiffFetch
{
    /// <summary>
    /// The file in memory, its transfer reported and its metadata parsed from the copy; null when
    /// it is too large to hold that way. The caller disposes it.
    /// </summary>
    public static NativeFileBuffer? IntoMemory(string path, Composite composite, CancellationToken ct)
    {
        if (!NativeFileBuffer.ShouldBuffer(composite.FileSizeBytes ?? 0))
            return null;

        var data = NativeFileBuffer.Read(path, ct, out var readMs, composite.ReportTransferred);

        try
        {
            composite.CompleteTransfer((long)data.Length, readMs);
            ct.ThrowIfCancellationRequested();

            using var metadata = data.AsStream();
            composite.ExifInfo = MetadataProcessor.ParseMetadata(metadata, path);

            return data;
        }
        catch
        {
            data.Dispose();
            throw;
        }
    }
}