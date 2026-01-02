namespace Lyra.Imaging.Psd.Parser.Primitives;

public static class PsdSignatures
{
    // Big-endian uints of the ASCII FourCC codes
    public const uint FileHeader = 0x38425053;            // "8BPS"
    public const uint ImageResources = 0x3842494D;        // "8BIM"

    public const uint PhotoshopBlock = 0x3842494D;        // "8BIM"
    public const uint PhotoshopLargeBlock = 0x38423634;   // "8B64"

    public static bool IsPhotoshopBlock(uint sig) => sig is PhotoshopBlock or PhotoshopLargeBlock;
}