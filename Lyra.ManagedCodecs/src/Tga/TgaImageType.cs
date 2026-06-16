namespace Lyra.ManagedCodecs.Tga;

/// <summary>
/// The TGA image type (header byte 2).
/// </summary>
internal enum TgaImageType : byte
{
    NoImageData = 0,
    ColorMapped = 1,
    TrueColor = 2,
    BlackAndWhite = 3,
    RleColorMapped = 9,
    RleTrueColor = 10,
    RleBlackAndWhite = 11,
}

internal static class TgaImageTypeExtensions
{
    public static bool IsValid(this TgaImageType type) => type
        is TgaImageType.NoImageData
        or TgaImageType.ColorMapped
        or TgaImageType.TrueColor
        or TgaImageType.BlackAndWhite
        or TgaImageType.RleColorMapped
        or TgaImageType.RleTrueColor
        or TgaImageType.RleBlackAndWhite;

    public static bool IsRunLengthEncoded(this TgaImageType type) => type
        is TgaImageType.RleColorMapped
        or TgaImageType.RleTrueColor
        or TgaImageType.RleBlackAndWhite;

    public static bool IsColorMapped(this TgaImageType type) => type
        is TgaImageType.ColorMapped
        or TgaImageType.RleColorMapped;

    public static bool IsGrayscale(this TgaImageType type) => type
        is TgaImageType.BlackAndWhite
        or TgaImageType.RleBlackAndWhite;
}