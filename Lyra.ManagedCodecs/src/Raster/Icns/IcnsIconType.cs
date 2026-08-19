namespace Lyra.ManagedCodecs.Raster.Icns;

public enum IcnsPayloadKind
{
    Embedded,
    Argb,
    Rle24,
    Mask
}

public readonly record struct IcnsIconType(string Code, int Width, int Height, int Scale, IcnsPayloadKind Kind)
{
    private static readonly Dictionary<string, IcnsIconType> ByCode = Build();

    public static bool TryGet(string code, out IcnsIconType type) => ByCode.TryGetValue(code, out type);
    
    public static string? MaskCodeFor(int width) => width switch
    {
        16 => "s8mk",
        32 => "l8mk",
        48 => "h8mk",
        128 => "t8mk",
        _ => null
    };

    private static Dictionary<string, IcnsIconType> Build()
    {
        IcnsIconType[] types =
        [
            new("is32",  16,  16, 1, IcnsPayloadKind.Rle24),
            new("il32",  32,  32, 1, IcnsPayloadKind.Rle24),
            new("ih32",  48,  48, 1, IcnsPayloadKind.Rle24),
            new("it32", 128, 128, 1, IcnsPayloadKind.Rle24),

            new("s8mk",  16,  16, 1, IcnsPayloadKind.Mask),
            new("l8mk",  32,  32, 1, IcnsPayloadKind.Mask),
            new("h8mk",  48,  48, 1, IcnsPayloadKind.Mask),
            new("t8mk", 128, 128, 1, IcnsPayloadKind.Mask),

            new("ic04", 16, 16, 1, IcnsPayloadKind.Argb),
            new("ic05", 32, 32, 2, IcnsPayloadKind.Argb),

            new("icp4",   16,   16, 1, IcnsPayloadKind.Embedded),
            new("icp5",   32,   32, 1, IcnsPayloadKind.Embedded),
            new("icp6",   64,   64, 1, IcnsPayloadKind.Embedded),
            new("ic07",  128,  128, 1, IcnsPayloadKind.Embedded),
            new("ic08",  256,  256, 1, IcnsPayloadKind.Embedded),
            new("ic09",  512,  512, 1, IcnsPayloadKind.Embedded),
            new("ic10", 1024, 1024, 2, IcnsPayloadKind.Embedded),
            new("ic11",   32,   32, 2, IcnsPayloadKind.Embedded),
            new("ic12",   64,   64, 2, IcnsPayloadKind.Embedded),
            new("ic13",  256,  256, 2, IcnsPayloadKind.Embedded),
            new("ic14",  512,  512, 2, IcnsPayloadKind.Embedded),

            new("icsb", 18, 18, 1, IcnsPayloadKind.Embedded),
            new("icsB", 36, 36, 2, IcnsPayloadKind.Embedded),
            new("sb24", 24, 24, 1, IcnsPayloadKind.Embedded),
            new("SB24", 48, 48, 2, IcnsPayloadKind.Embedded)
        ];

        return types.ToDictionary(t => t.Code, StringComparer.Ordinal);
    }
}