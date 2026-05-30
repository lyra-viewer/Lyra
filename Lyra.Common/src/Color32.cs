using System.Globalization;

namespace Lyra.Common;

/// <summary>
/// Engine-neutral 8-bit-per-channel RGBA color. Pure data arsed from a <c>#RRGGBB</c> or <c>#RRGGBBAA</c> hex string;
/// the 6-digit form is treated as fully opaque (alpha = 255).
/// </summary>
public readonly record struct Color32(byte R, byte G, byte B, byte A)
{
    public static bool TryParse(string? hex, out Color32 color)
    {
        color = default;

        if (string.IsNullOrWhiteSpace(hex))
            return false;

        var span = hex.AsSpan().Trim();
        if (span.Length > 0 && span[0] == '#')
            span = span[1..];

        if (span.Length != 6 && span.Length != 8)
            return false;

        if (!TryParseHex(span[..2], out var r) || !TryParseHex(span[2..4], out var g) || !TryParseHex(span[4..6], out var b))
            return false;

        byte a = 255;
        if (span.Length == 8 && !TryParseHex(span[6..8], out a))
            return false;

        color = new Color32(r, g, b, a);
        return true;
    }

    private static bool TryParseHex(ReadOnlySpan<char> hex, out byte value)
        => byte.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
}