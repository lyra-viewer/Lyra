using Lyra.Imaging.Psd.Parser.Common;
using Lyra.Imaging.Psd.Parser.PsdReader;
using Lyra.Imaging.Psd.Parser.SectionData;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Lyra.Imaging.Psd.Parser.Decompressors;

public abstract class PsdDecompressorBase : IPsdDecompressor
{
    public abstract CompressionType Compression { get; }

    public virtual void ValidatePayload(FileHeader header, ImageData imageData)
    {
        // Default: nothing to validate for most compression types.
        // RLE overrides this to validate its row-byte-count table / total payload.
    }

    public void Decompress(PsdBigEndianReader reader, ImageFrame<Rgba32> frame, FileHeader header, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(frame);

        Console.WriteLine($"PSD composite decompress: {Compression}.");
        
        ct.ThrowIfCancellationRequested();

        ValidateCommonInputs(frame, header);
        
        switch (header.Depth)
        {
            case 8:
                Decompress8(reader, frame, header, ct);
                return;
            case 16:
                Decompress16(reader, frame, header, ct);
                return;
            case 32:
                Decompress32(reader, frame, header, ct);
                return;
            default:
                throw new NotSupportedException($"PSD composite decompress: Depth {header.Depth} not supported. Expected 8, 16, or 32.");
        }
    }
    
    protected abstract void Decompress8(PsdBigEndianReader reader, ImageFrame<Rgba32> frame, FileHeader header, CancellationToken ct);
    
    protected virtual void Decompress16(PsdBigEndianReader reader, ImageFrame<Rgba32> frame, FileHeader header, CancellationToken ct)
        => throw new NotSupportedException($"PSD composite decompress ({Compression}): 16-bit depth not implemented.");
    
    protected virtual void Decompress32(PsdBigEndianReader reader, ImageFrame<Rgba32> frame, FileHeader header, CancellationToken ct)
        => throw new NotSupportedException($"PSD composite decompress ({Compression}): 32-bit depth not implemented.");
    
    protected static void ValidateCommonInputs(ImageFrame<Rgba32> frame, FileHeader header)
    {
        if (header.Width <= 0 || header.Height <= 0)
            throw new InvalidOperationException($"Invalid PSD dimensions: {header.Width}x{header.Height}.");

        if (frame.Width != header.Width || frame.Height != header.Height)
            throw new InvalidOperationException(
                $"Frame size {frame.Width}x{frame.Height} does not match header {header.Width}x{header.Height}.");

        if (header.NumberOfChannels < 3)
            throw new InvalidOperationException($"PSD has {header.NumberOfChannels} channels; expected at least 3 (RGB).");

        if (header.ColorMode != ColorMode.Rgb)
            throw new NotSupportedException($"PSD composite decode: ColorMode {header.ColorMode} not supported yet.");
    }
}