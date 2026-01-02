namespace Lyra.Imaging.Psd.Parser.SectionData;

public readonly record struct AdditionalLayerInformation(
    string Signature,   // "8BIM" or "8B64"
    uint Key,           // 4-char key like "luni", "lyid"
    long PayloadOffset,
    long PayloadLength
)
{
    public string KeyFourCC => new(new[]
    {
        (char)(Key >> 24 & 0xFF),
        (char)(Key >> 16 & 0xFF),
        (char)(Key >> 8 & 0xFF),
        (char)(Key & 0xFF)
    });
}