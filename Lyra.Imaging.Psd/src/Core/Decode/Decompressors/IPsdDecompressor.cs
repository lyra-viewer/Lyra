using Lyra.Imaging.Psd.Core.Decode.Pixel;
using Lyra.Imaging.Psd.Core.Readers;
using Lyra.Imaging.Psd.Core.SectionData;

namespace Lyra.Imaging.Psd.Core.Decode.Decompressors;

public interface IPsdDecompressor
{
    PlaneImage DecompressPreview(PsdBigEndianReader reader, FileHeader header, int width, int height, CancellationToken ct);

    void DecompressPlanesRowRegion(PsdBigEndianReader reader, FileHeader header, int yStart, int yEnd, IPlaneRowConsumer consumer, CancellationToken ct);
}