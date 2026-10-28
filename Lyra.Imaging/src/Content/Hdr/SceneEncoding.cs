namespace Lyra.Imaging.Content.Hdr;

/// <summary>How a texture holding scene-referred light stores it.</summary>
public enum SceneEncoding
{
    /// <summary>Half-float, the light itself.</summary>
    Linear = 0,

    /// <summary>
    /// 8 bits per channel, see <see cref="CompressedLight"/>. For images too large to keep as
    /// half-float; the renderer expands it back to light before the tone curve.
    /// </summary>
    Compressed = 1
}

/// <summary>
/// The <see cref="SceneEncoding.Compressed"/> form: each channel as <c>x / (1 + x)</c>, then the
/// sRGB transfer, in 8 bits. Invertible, so exposure and curve stay live; at exposure 0 it is
/// about as precise as an 8-bit tone-mapped image. The shader holds the inverse too.
/// </summary>
public static class CompressedLight
{
    /// <summary>The light the top code expands to; the range above it is lost.</summary>
    public const float Ceiling = 1023f;

    public static float Compress(float light)
    {
        if (!(light > 0f))
            return 0f;

        // 1 - 1/(1+x) rather than x/(1+x), so +Inf lands on 1 instead of NaN.
        return 1f - (1f / (1f + light));
    }

    public static float Expand(float compressed)
    {
        var v = Math.Clamp(compressed, 0f, Ceiling / (Ceiling + 1f));
        return v / (1f - v);
    }

    public static byte Encode(float light) => (byte)Math.Clamp(MathF.Round(SrgbEncode(Compress(light)) * 255f), 0f, 255f);

    public static float Decode(byte stored) => Expand(SrgbDecode(stored / 255f));

    internal static float SrgbEncode(float linear) =>
        linear <= 0.0031308f ? linear * 12.92f : (1.055f * MathF.Pow(linear, 1f / 2.4f)) - 0.055f;

    private static float SrgbDecode(float encoded) =>
        encoded <= 0.04045f ? encoded / 12.92f : MathF.Pow((encoded + 0.055f) / 1.055f, 2.4f);
}
