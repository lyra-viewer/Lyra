namespace Lyra.Imaging.Psd.Core.Primitives;

public static class PsdSignatures
{
    // File signatures
    public const uint FileHeader = 0x38425053;            // "8BPS"

    // Section signatures
    public const uint ImageResources = 0x3842494D;        // "8BIM"
    public const uint PhotoshopBlock = 0x3842494D;        // "8BIM"
    public const uint PhotoshopLargeBlock = 0x38423634;   // "8B64"
    
    // Image Resource IDs
    public const ushort IccProfileResourceId = 0x040F;
    
    // ICC tag signatures
    public const uint IccTagDesc = 0x64657363;            // "desc"
    public const uint IccTypeDesc = 0x64657363;           // "desc"
    public const uint IccTypeMluc = 0x6D6C7563;           // "mluc"

    public static bool IsPhotoshopBlock(uint sig) => sig is PhotoshopBlock or PhotoshopLargeBlock;
}