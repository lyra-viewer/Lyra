using System.Buffers;
using System.IO.Compression;
using Lyra.Imaging.Psd.Parser.Common;
using Lyra.Imaging.Psd.Parser.PsdReader;
using Lyra.Imaging.Psd.Parser.SectionData;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.PixelFormats;

namespace Lyra.Imaging.Psd.Parser.Decompressors;

internal class PsdZipDecompressor : PsdDecompressorBase
{
    public override CompressionType Compression => CompressionType.Zip;

    protected override void Decompress8(PsdBigEndianReader reader, ImageFrame<Rgba32> frame, FileHeader header, CancellationToken ct)
    {
        var width = header.Width;
        var height = header.Height;
        var channelsToRead = Math.Min((int)header.NumberOfChannels, 4);

        var source = reader.BaseStream;

        // In PSD "ZIP" compression, the image data is zlib-compressed.
        using var z = new ZLibStream(source, CompressionMode.Decompress, leaveOpen: true);

        var rowBuf = ArrayPool<byte>.Shared.Rent(width);
        try
        {
            for (var channel = 0; channel < channelsToRead; channel++)
            {
                ct.ThrowIfCancellationRequested();

                for (var y = 0; y < height; y++)
                {
                    ct.ThrowIfCancellationRequested();

                    ReadExactly(z, rowBuf, width, ct);

                    var dst = frame.DangerousGetPixelRowMemory(y).Span;

                    switch (channel)
                    {
                        case 0:
                            for (var x = 0; x < width; x++)
                                dst[x].R = rowBuf[x];
                            break;
                        case 1:
                            for (var x = 0; x < width; x++)
                                dst[x].G = rowBuf[x];
                            break;
                        case 2:
                            for (var x = 0; x < width; x++)
                                dst[x].B = rowBuf[x];
                            break;
                        case 3:
                            for (var x = 0; x < width; x++)
                                dst[x].A = rowBuf[x];
                            break;
                    }
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rowBuf);
        }
    }

    private static void ReadExactly(Stream s, byte[] buffer, int count, CancellationToken ct)
    {
        var offset = 0;
        while (offset < count)
        {
            ct.ThrowIfCancellationRequested();
            var read = s.Read(buffer, offset, count - offset);
            if (read <= 0)
                throw new EndOfStreamException($"Unexpected end of ZIP stream. Needed {count} bytes, got {offset}.");
            
            offset += read;
        }
    }
}