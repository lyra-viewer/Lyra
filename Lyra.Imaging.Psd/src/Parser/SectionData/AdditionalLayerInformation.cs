using Lyra.Imaging.Psd.Parser.Primitives;

namespace Lyra.Imaging.Psd.Parser.SectionData;

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