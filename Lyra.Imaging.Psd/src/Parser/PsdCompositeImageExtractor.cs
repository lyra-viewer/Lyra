using Lyra.Imaging.Psd.Parser.Common;
using Lyra.Imaging.Psd.Parser.Decompressors;
using Lyra.Imaging.Psd.Parser.PsdReader;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.PixelFormats;

namespace Lyra.Imaging.Psd.Parser;

public static class PsdCompositeImageExtractor
{
    private static readonly Dictionary<CompressionType, Func<IPsdDecompressor>> DecompressorFactories = new()
    {
        { CompressionType.Raw, () => new PsdRawDecompressor() },
        { CompressionType.Rle, () => new PsdRleDecompressor() },
        { CompressionType.Zip, () => new PsdZipDecompressor() },
        { CompressionType.ZipPredict, () => new PsdZipPredictDecompressor() }
    };

    public static Image<Rgba32> DecodeRgba32(PsdDocument psdDocument, Stream stream, CancellationToken cancellationToken)
    {
        var header = psdDocument.FileHeader;
        var imageData = psdDocument.ImageData;

        var width = header.Width;
        var height = header.Height;

        if (!stream.CanSeek)
            throw new NotSupportedException("PSD composite decode currently requires a seekable stream.");

        // IMPORTANT INVARIANT:
        // ImageData.PayloadOffset points to the first byte AFTER the 2-byte compression field.
        stream.Position = imageData.PayloadOffset;

        var reader = new PsdBigEndianReader(stream);

        // Allocate output image
        var image = new Image<Rgba32>(width, height);
        var frame = image.Frames.RootFrame;

        // Initialize destination to black, opaque (alpha=255). RGB will get overwritten anyway.
        InitializeOpaque(frame, height, cancellationToken);

        try
        {
            if (!DecompressorFactories.TryGetValue(imageData.CompressionType, out var factory))
                throw new NotSupportedException($"Compression {imageData.CompressionType} not supported.");

            var decompressor = factory();
            decompressor.ValidatePayload(header, imageData);
            decompressor.Decompress(reader, frame, header, cancellationToken);
            return image;
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    private static void InitializeOpaque(ImageFrame<Rgba32> frame, int height, CancellationToken ct)
    {
        var opaqueBlack = new Rgba32(0, 0, 0, 255);
        for (var y = 0; y < height; y++)
        {
            ct.ThrowIfCancellationRequested();
            var row = frame.DangerousGetPixelRowMemory(y).Span;
            row.Fill(opaqueBlack);
        }
    }
}