using Lyra.Common;
using SkiaSharp;

namespace Lyra.Renderer.Drawing;

/// <summary>
/// The linear RGB matrix taking an image's primaries to the display's, and the destination's
/// luminance weights.
/// </summary>
internal static class GamutTransform
{
    /// Column-major, as SkSL wants a float3x3 uniform.
    private static readonly float[] Identity = [1, 0, 0, 0, 1, 0, 0, 0, 1];

    private static readonly Lock Gate = new();
    private static SKColorSpace? _lastSource;
    private static SKColorSpace? _lastDestination;
    private static float[] _lastMatrix = Identity;

    /// <summary>
    /// Source primaries to destination primaries, ready to upload as a <c>float3x3</c>. Falls back
    /// to identity for a color space that cannot be expressed as a matrix, such as an ICC profile
    /// carrying a lookup table.
    /// </summary>
    public static float[] Between(SKColorSpace? source, SKColorSpace? destination)
    {
        if (source is null || destination is null || ReferenceEquals(source, destination))
            return (float[])Identity.Clone();

        lock (Gate)
        {
            if (!ReferenceEquals(source, _lastSource) || !ReferenceEquals(destination, _lastDestination))
            {
                _lastMatrix = Build(source, destination);
                _lastSource = source;
                _lastDestination = destination;
            }

            return (float[])_lastMatrix.Clone();
        }
    }

    /// <summary>
    /// How much each of the destination's primaries contributes to luminance - the Y row of its
    /// RGB -> XYZ matrix. Rec.709's weights are not Display-P3's, and the wrong ones tilt every
    /// tone-mapped color toward an over-weighted primary.
    /// </summary>
    public static float[] LuminanceWeights(SKColorSpace? destination)
    {
        if (destination is null || !destination.ToColorSpaceXyz(out var toXyz))
            return [0.2126f, 0.7152f, 0.0722f];

        // [column, row] - row 1 is Y, one entry per primary.
        return [toXyz[0, 1], toXyz[1, 1], toXyz[2, 1]];
    }

    private static float[] Build(SKColorSpace source, SKColorSpace destination)
    {
        if (!source.ToColorSpaceXyz(out var sourceToXyz) || !destination.ToColorSpaceXyz(out var destinationToXyz))
        {
            Logger.Warning("[GamutTransform] A colour space could not be expressed as a matrix; drawing HDR content without a gamut conversion.");
            return Identity;
        }

        // Both matrices go RGB -> XYZ(D50), so destination^-1 * source goes source RGB -> dest RGB.
        var toDestination = SKColorSpaceXyz.Concat(destinationToXyz.Invert(), sourceToXyz);

        // SkSL takes a float3x3 column by column and SKColorSpaceXyz indexes [column, row], so this
        // reads out in its natural order. Transposing it still maps red near red, but stops mapping
        // neutral to neutral and tints the image green.
        return
        [
            toDestination[0, 0], toDestination[0, 1], toDestination[0, 2],
            toDestination[1, 0], toDestination[1, 1], toDestination[1, 2],
            toDestination[2, 0], toDestination[2, 1], toDestination[2, 2]
        ];
    }
}
