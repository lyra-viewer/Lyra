using Wacton.Unicolour;
using Wacton.Unicolour.Icc;

namespace Lyra.Imaging.Psd.Core.Decode.ColorCalibration;

/// <summary>
/// Helper used during ICC calibration sampling to query the "oracle"
/// (Unicolour + ICC) for the expected RGB output.
/// </summary>
internal static class IccOracle
{
    public static (int r, int g, int b) OracleIccRgb(Configuration config, byte r, byte g, byte b)
    {
        var u = new Unicolour(
            config,
            new Channels(r / 255.0, g / 255.0, b / 255.0));

        var rgb = u.Rgb.Byte255;
        return (Clamp(rgb.R), Clamp(rgb.G), Clamp(rgb.B));
    }

    public static (int r, int g, int b) OracleIccRgb(Configuration config, byte c, byte m, byte y, byte k, bool invert)
    {
        var u = new Unicolour(
            config,
            new Channels(ToUnit(c, invert), ToUnit(m, invert), ToUnit(y, invert), ToUnit(k, invert)));

        var rgb = u.Rgb.Byte255;
        return (Clamp(rgb.R), Clamp(rgb.G), Clamp(rgb.B));
    }

    private static double ToUnit(byte v, bool invert) => (invert ? 255 - v : v) / 255.0;

    private static int Clamp(int v) => v < 0 ? 0 : v > 255 ? 255 : v;
}