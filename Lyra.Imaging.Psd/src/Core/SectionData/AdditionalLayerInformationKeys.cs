using Lyra.Imaging.Psd.Core.Primitives;

namespace Lyra.Imaging.Psd.Core.SectionData;

internal static class AdditionalLayerInformationKeys
{
    public static readonly uint UnicodeLayerName = FourCC.FromString("luni");
    public static readonly uint LayerId          = FourCC.FromString("lyid");
    public static readonly uint SectionDivider   = FourCC.FromString("lsct");
    public static readonly uint BlendClipping    = FourCC.FromString("clbl");
    public static readonly uint Protected        = FourCC.FromString("lspf");

    // PSB: these keys use an 8-byte (uint64) length field instead of 4-byte (uint32).
    public static bool UsesLongLengthFieldInPsb(uint key) =>
        key == FourCC.FromString("LMFX") ||
        key == FourCC.FromString("LFX2") ||
        key == FourCC.FromString("PATT") ||
        key == FourCC.FromString("PAT2") ||
        key == FourCC.FromString("FMsp") ||
        key == FourCC.FromString("Shmd");
}