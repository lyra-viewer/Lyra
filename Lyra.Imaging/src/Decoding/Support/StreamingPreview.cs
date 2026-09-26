using Lyra.Common;
using SkiaSharp;

namespace Lyra.Imaging.Decoding.Support;

/// <summary>
/// Builds a downsampled preview of a large image without ever holding the whole of it.
/// </summary>
internal static class StreamingPreview
{
    /// <summary>Reads a band of full-width rows, or returns false.</summary>
    internal delegate bool BandReader(uint firstRow, uint rowCount, out IntPtr pixels, out uint stride);
    
    /// <summary>
    /// How many source pixels each preview pixel is averaged from, per axis, at most.
    /// </summary>
    internal const int MaxSamplesPerAxis = 4;

    /// <summary>
    /// Streams <paramref name="width"/> x <paramref name="height"/> into a preview no larger than
    /// the given bounds.
    /// </summary>
    /// <param name="read">Reads a band; the caller owns the buffer and frees it after each call.</param>
    /// <param name="release">Frees what <paramref name="read"/> returned.</param>
    /// <param name="rowsPerBand">
    /// How many rows to ask for at a time. Only affects peak memory and call overhead - the
    /// underlying strips are decoded whole regardless.
    /// </param>
    /// <param name="channels">Bytes per pixel the band reader produces: 1 for gray, 4 for RGBA.</param>
    /// <param name="premultiplied">
    /// Whether the color channels arrive already multiplied by alpha, which only changes how the
    /// result is tagged - averaging associated values is correct as it stands, and averaging
    /// unassociated ones across differing alpha is what would not be.
    /// </param>
    public static SKBitmap? Build(int width, int height, int maxWidth, int maxHeight, BandReader read, Action<IntPtr> release, uint rowsPerBand, int channels, CancellationToken ct, bool premultiplied = false)
    {
        var scale = MathF.Min(1f, MathF.Min(maxWidth / (float)width, maxHeight / (float)height));

        var previewWidth = Math.Max(1, (int)(width * scale));
        var previewHeight = Math.Max(1, (int)(height * scale));
        
        var strideY = StrideFor(height, previewHeight);
        var strideX = StrideFor(width, previewWidth);
        
        var rowSums = new long[(long)previewWidth * channels];
        var rowCounts = new int[previewWidth];

        var bitmap = new SKBitmap(new SKImageInfo(previewWidth, previewHeight,
            channels == 1 ? SKColorType.Gray8 : SKColorType.Rgba8888,
            channels == 1 ? SKAlphaType.Opaque : premultiplied ? SKAlphaType.Premul : SKAlphaType.Unpremul));
        
        var runStart = new int[previewWidth + 1];
        for (var pc = 0; pc <= previewWidth; pc++)
            runStart[pc] = (int)Math.Min(width, (long)pc * width / previewWidth);

        var currentRow = -1;

        try
        {
            for (uint bandTop = 0; bandTop < height; bandTop += rowsPerBand)
            {
                ct.ThrowIfCancellationRequested();

                var rows = (uint)Math.Min(rowsPerBand, height - bandTop);
                if (!read(bandTop, rows, out var pixels, out var stride) || pixels == IntPtr.Zero)
                {
                    Logger.Warning($"[StreamingPreview] Could not read rows {bandTop}..{bandTop + rows}; the preview will be short.");
                    bitmap.Dispose();
                    return null;
                }

                try
                {
                    Accumulate(pixels, stride, bandTop, rows, height, previewWidth, previewHeight, strideY, strideX, channels, runStart, rowSums, rowCounts, bitmap, ref currentRow);
                }
                finally
                {
                    release(pixels);
                }
            }

            Flush(bitmap, previewWidth, channels, currentRow, rowSums, rowCounts);
            FillUnwritten(bitmap, previewWidth, previewHeight, channels, currentRow);

            bitmap.SetImmutable();
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    internal static int StrideFor(int source, int preview)
    {
        if (preview <= 0 || source <= preview)
            return 1;

        return Math.Max(1, (int)Math.Ceiling((double)source / preview / MaxSamplesPerAxis));
    }

    private static unsafe void Accumulate(
        IntPtr pixels, uint stride, uint bandTop, uint rows, int height,
        int previewWidth, int previewHeight, int strideY, int strideX, int channels,
        int[] runStart, long[] rowSums, int[] rowCounts, SKBitmap bitmap, ref int currentRow)
    {
        var band = (byte*)pixels;

        fixed (long* sums = rowSums)
        fixed (int* counts = rowCounts)
        fixed (int* runs = runStart)
        {
            for (uint r = 0; r < rows; r++)
            {
                var sourceRow = bandTop + r;
                if (sourceRow % (uint)strideY != 0)
                    continue;

                var previewRow = Math.Min(previewHeight - 1, (int)((long)sourceRow * previewHeight / height));
                if (previewRow != currentRow)
                {
                    Flush(bitmap, previewWidth, channels, currentRow, rowSums, rowCounts);

                    Array.Clear(rowSums);
                    Array.Clear(rowCounts);
                    currentRow = previewRow;
                }

                var src = band + (long)r * stride;

                for (var pc = 0; pc < previewWidth; pc++)
                {
                    var from = runs[pc];
                    var to = runs[pc + 1];

                    if (channels == 1)
                    {
                        long acc = 0;
                        var taken = 0;
                        for (var x = from; x < to; x += strideX)
                        {
                            acc += src[x];
                            taken++;
                        }

                        sums[pc] += acc;
                        counts[pc] += taken;
                        continue;
                    }

                    var target = sums + (long)pc * channels;
                    var count = 0;

                    for (var x = from; x < to; x += strideX)
                    {
                        var sample = src + (long)x * channels;
                        for (var c = 0; c < channels; c++)
                            target[c] += sample[c];

                        count++;
                    }

                    counts[pc] += count;
                }
            }
        }
    }

    /// <summary>Writes one finished output row. A no-op before the first row has started.</summary>
    private static unsafe void Flush(SKBitmap bitmap, int width, int channels, int row, long[] sums, int[] counts)
    {
        if (row < 0)
            return;

        var dst = (byte*)bitmap.GetPixels() + (long)row * bitmap.RowBytes;
        for (var x = 0; x < width; x++)
        {
            var n = counts[x];
            if (channels == 1)
            {
                dst[x] = n > 0 ? (byte)(sums[x] / n) : (byte)255;
                continue;
            }

            var target = dst + (long)x * channels;
            var source = (long)x * channels;
            for (var c = 0; c < channels; c++)
                target[c] = n > 0 ? (byte)(sums[source + c] / n) : (byte)255;
        }
    }

    /// <summary>
    /// Paints any rows no source row mapped to.
    /// </summary>
    private static unsafe void FillUnwritten(SKBitmap bitmap, int width, int height, int channels, int lastRow)
    {
        var dst = (byte*)bitmap.GetPixels();

        for (var y = lastRow + 1; y < height; y++)
            new Span<byte>(dst + (long)y * bitmap.RowBytes, width * channels).Fill(255);
    }
}