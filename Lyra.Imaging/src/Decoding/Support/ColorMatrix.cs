using Lyra.Imaging.Content;
using SkiaSharp;

namespace Lyra.Imaging.Decoding.Support;

/// <summary>Linear RGB matrices between colour spaces, from Skia's own XYZ (D50) definitions.</summary>
internal static class ColorMatrix
{
    /// <summary>Linear sRGB into the linear ProPhoto a camera tone curve is defined on.</summary>
    public static readonly float[] ToCurveSpace = Required(SKColorSpace.CreateSrgbLinear(), CameraToneCurve.WorkingSpace);

    /// <summary>Linear ProPhoto back to linear sRGB.</summary>
    public static readonly float[] FromCurveSpace = Required(CameraToneCurve.WorkingSpace, SKColorSpace.CreateSrgbLinear());

    private static float[] Required(SKColorSpace source, SKColorSpace destination) =>
        Between(source, destination) ?? throw new InvalidOperationException("Both spaces are matrix spaces by construction.");

    /// <summary>
    /// Row-major: <c>out[r] = m[3r] * in[0] + m[3r + 1] * in[1] + m[3r + 2] * in[2]</c>. Null for a
    /// space that is not a matrix.
    /// </summary>
    public static float[]? Between(SKColorSpace source, SKColorSpace destination)
    {
        if (!source.ToColorSpaceXyz(out var sourceToXyz) || !destination.ToColorSpaceXyz(out var destinationToXyz))
            return null;

        var m = SKColorSpaceXyz.Concat(destinationToXyz.Invert(), sourceToXyz);

        // SKColorSpaceXyz indexes [column, row].
        return
        [
            m[0, 0], m[1, 0], m[2, 0],
            m[0, 1], m[1, 1], m[2, 1],
            m[0, 2], m[1, 2], m[2, 2]
        ];
    }
}