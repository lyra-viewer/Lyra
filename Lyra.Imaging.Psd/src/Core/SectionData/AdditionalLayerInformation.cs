using Lyra.Imaging.Psd.Core.Primitives;

namespace Lyra.Imaging.Psd.Core.SectionData;

public readonly record struct AdditionalLayerInformation(
    uint Signature,
    uint Key,
    long PayloadOffset,
    long PayloadLength
)
{
    public string KeyFourCC => FourCC.ToString(Key);
    public string SignatureFourCC => FourCC.ToString(Signature);
}