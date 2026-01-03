using System.Buffers;
using Lyra.Imaging.Psd.Parser.Common;
using Lyra.Imaging.Psd.Parser.PsdReader;
using Lyra.Imaging.Psd.Parser.SectionData;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.PixelFormats;

namespace Lyra.Imaging.Psd.Parser.Decompressors;

internal class PsdRawDecompressor : PsdDecompressorBase
{
    public override CompressionType Compression => CompressionType.Raw;

    protected override void Decompress8(PsdBigEndianReader reader, ImageFrame<Rgba32> frame, FileHeader header, CancellationToken ct)
    {
        var width = header.Width;
        var height = header.Height;
        var channels = header.NumberOfChannels;

        // Only use the first 4 channels (RGB + optional alpha) for now.
        // Extra channels (spot channels) exist; ignore them for MVP.
        var usedChannels = Math.Min((int)channels, 4);
        
        var rented = ArrayPool<byte>.Shared.Rent(width);
        try
        {
            for (var ch = 0; ch < usedChannels; ch++)
            {
                for (var y = 0; y < height; y++)
                {
                    ct.ThrowIfCancellationRequested();

                    reader.ReadExactly(rented.AsSpan(0, width));
                    var src = rented.AsSpan(0, width);

                    var row = frame.DangerousGetPixelRowMemory(y).Span;
                    switch (ch)
                    {
                        case 0:
                            for (var x = 0; x < width; x++) row[x].R = src[x];
                            break;
                        case 1:
                            for (var x = 0; x < width; x++) row[x].G = src[x];
                            break;
                        case 2:
                            for (var x = 0; x < width; x++) row[x].B = src[x];
                            break;
                        case 3:
                            for (var x = 0; x < width; x++) row[x].A = src[x];
                            break;
                    }
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }
}