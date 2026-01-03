using System.Buffers;
using Lyra.Imaging.Psd.Parser.Common;
using Lyra.Imaging.Psd.Parser.PsdReader;
using Lyra.Imaging.Psd.Parser.SectionData;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.PixelFormats;

namespace Lyra.Imaging.Psd.Parser.Decompressors;

internal class PsdRleDecompressor : PsdDecompressorBase
{
    // Safety cap for corrupt files (PackBits row length table is trusted input).
    private const int MaxPackedRowBytes = 16 * 1024 * 1024;

    public override CompressionType Compression => CompressionType.Rle;

    private ImageData? _imageData; // set by ValidatePayload

    public override void ValidatePayload(FileHeader header, ImageData imageData)
    {
        _imageData = imageData;
    }

    protected override void Decompress8(PsdBigEndianReader reader, ImageFrame<Rgba32> frame, FileHeader header, CancellationToken ct)
    {
        var width = header.Width;
        var height = header.Height;
        var channels = header.NumberOfChannels;
        var version = header.Version;

        // Only use the first 4 channels (RGB + optional alpha) for now.
        // Extra channels (spot channels) exist; ignore them for MVP.
        var usedChannels = Math.Min((int)channels, 4);

        var rowCount = usedChannels * height;
        var rowByteCounts = new int[rowCount];
        for (var i = 0; i < rowCount; i++)
        {
            ct.ThrowIfCancellationRequested();

            rowByteCounts[i] = (version == 2)
                ? reader.ReadInt32()
                : reader.ReadUInt16();

            if ((uint)rowByteCounts[i] > MaxPackedRowBytes)
                throw new InvalidOperationException($"Suspicious PackBits row length: {rowByteCounts[i]} bytes.");
        }

        if (_imageData is null)
            throw new InvalidOperationException("ValidatePayload must be called before Decompress for RLE.");

        ValidateRlePayload((ImageData)_imageData, usedChannels, height, rowByteCounts, version);

        var packedRent = ArrayPool<byte>.Shared.Rent(64 * 1024);
        var unpackedRent = ArrayPool<byte>.Shared.Rent(width);
        try
        {
            var rowIndex = 0;
            for (var ch = 0; ch < usedChannels; ch++)
            {
                for (var y = 0; y < height; y++, rowIndex++)
                {
                    ct.ThrowIfCancellationRequested();

                    var packedLen = rowByteCounts[rowIndex];
                    if (packedLen < 0)
                        throw new InvalidOperationException($"Negative PackBits row length: {packedLen} (corrupt PSD).");

                    if (packedLen > packedRent.Length)
                    {
                        ArrayPool<byte>.Shared.Return(packedRent);
                        packedRent = ArrayPool<byte>.Shared.Rent(packedLen);
                    }

                    reader.ReadExactly(packedRent.AsSpan(0, packedLen));

                    var dst = unpackedRent.AsSpan(0, width);
                    PackBitsDecode(packedRent.AsSpan(0, packedLen), dst);

                    var row = frame.DangerousGetPixelRowMemory(y).Span;
                    switch (ch)
                    {
                        case 0:
                            for (var x = 0; x < width; x++) row[x].R = dst[x];
                            break;
                        case 1:
                            for (var x = 0; x < width; x++) row[x].G = dst[x];
                            break;
                        case 2:
                            for (var x = 0; x < width; x++) row[x].B = dst[x];
                            break;
                        case 3:
                            for (var x = 0; x < width; x++) row[x].A = dst[x];
                            break;
                    }
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(packedRent);
            ArrayPool<byte>.Shared.Return(unpackedRent);
        }
    }

    private static void PackBitsDecode(ReadOnlySpan<byte> src, Span<byte> dst)
    {
        var si = 0;
        var di = 0;

        while (di < dst.Length)
        {
            if (si >= src.Length)
                throw new InvalidOperationException("PackBits source exhausted before output row was filled.");

            var n = unchecked((sbyte)src[si++]);

            if (n >= 0)
            {
                var count = n + 1;

                if (si + count > src.Length)
                    throw new InvalidOperationException("PackBits literal overruns input.");
                if (di + count > dst.Length)
                    throw new InvalidOperationException("PackBits literal overruns output.");

                src.Slice(si, count).CopyTo(dst.Slice(di, count));
                si += count;
                di += count;
            }
            else if (n != -128)
            {
                var count = 1 - n;

                if (si >= src.Length)
                    throw new InvalidOperationException("PackBits replicate missing byte.");
                if (di + count > dst.Length)
                    throw new InvalidOperationException("PackBits replicate overruns output.");

                var val = src[si++];
                dst.Slice(di, count).Fill(val);
                di += count;
            }
            // n == -128 => NOP
        }
    }

    private static void ValidateRlePayload(ImageData imageData, int usedChannels, int height, int[] rowByteCounts, int version)
    {
        if (rowByteCounts.Length != usedChannels * height)
        {
            throw new InvalidOperationException(
                $"Invalid RLE table size: expected {usedChannels * height} entries, " +
                $"got {rowByteCounts.Length}.");
        }

        // PSD (v1): UInt16 per row
        // PSB (v2): Int32 per row
        var bytesPerEntry = version == 2 ? 4 : 2;
        var tableBytes = (long)usedChannels * height * bytesPerEntry;

        var sumPacked = 0L;
        for (var i = 0; i < rowByteCounts.Length; i++)
        {
            var len = rowByteCounts[i];
            if (len < 0)
                throw new InvalidOperationException($"Invalid RLE row length at index {i}: {len}.");

            if (len > MaxPackedRowBytes)
                throw new InvalidOperationException($"Suspicious RLE row length at index {i}: {len} bytes.");

            sumPacked += len;
        }

        var expectedPayload = tableBytes + sumPacked;
        if (expectedPayload != imageData.PayloadLength)
        {
            throw new InvalidOperationException(
                "Invalid PSD RLE composite payload:\n" +
                $"  Table bytes   : {tableBytes}\n" +
                $"  Packed bytes  : {sumPacked}\n" +
                $"  Expected total: {expectedPayload}\n" +
                $"  Actual payload: {imageData.PayloadLength}\n" +
                $"  Offset        : {imageData.PayloadOffset}\n" +
                $"  Version       : {(version == 2 ? "PSB" : "PSD")}");
        }
    }
}