using Lyra.Imaging.Psd.Core.Decode.Pixel;
using Lyra.Imaging.Psd.Core.Readers;
using Lyra.Imaging.Psd.Core.SectionData;

namespace Lyra.Imaging.Psd.Core.Decode.Decompressors;

public interface IPsdDecompressor
{
    void ValidatePayload(FileHeader header, ImageData imageData);

    PlaneImage DecompressPlanes(PsdBigEndianReader reader, FileHeader header, CancellationToken ct);
    
    PlaneImage DecompressPlanesScaled(PsdBigEndianReader reader, FileHeader header, int width, int height, CancellationToken ct);
}