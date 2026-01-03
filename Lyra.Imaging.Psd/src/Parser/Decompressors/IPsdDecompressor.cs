using Lyra.Imaging.Psd.Parser.PsdReader;
using Lyra.Imaging.Psd.Parser.SectionData;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Lyra.Imaging.Psd.Parser.Decompressors;

public interface IPsdDecompressor
{
    void ValidatePayload(FileHeader header, ImageData imageData);

    void Decompress(PsdBigEndianReader reader, ImageFrame<Rgba32> frame, FileHeader header, CancellationToken ct);
}