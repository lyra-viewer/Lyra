namespace Lyra.Imaging.Psd.Parser.SectionData;

internal static class AdditionalLayerInformationKeys
{
    public static readonly uint UnicodeLayerName = FourCC("luni");
    public static readonly uint LayerId          = FourCC("lyid");
    public static readonly uint SectionDivider   = FourCC("lsct");
    public static readonly uint BlendClipping    = FourCC("clbl");
    public static readonly uint Protected        = FourCC("lspf");
    
    // PSB: these keys use an 8-byte (uint64) length field instead of 4-byte (uint32).
    public static bool UsesLongLengthFieldInPsb(uint key) =>
        key == FourCC("LMFX") ||
        key == FourCC("LFX2") ||
        key == FourCC("PATT") ||
        key == FourCC("PAT2") ||
        key == FourCC("FMsp") ||
        key == FourCC("Shmd");

    private static uint FourCC(string s) => ((uint)s[0] << 24) | ((uint)s[1] << 16) | ((uint)s[2] << 8) | s[3];
}