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
        var r0 = r / 255.0;
        var g0 = g / 255.0;
        var b0 = b / 255.0;

        var u = new Unicolour(config, new Channels(r0, g0, b0));
        var rgb = u.Rgb.Byte255;

        return (Clamp0To255(rgb.R), Clamp0To255(rgb.G), Clamp0To255(rgb.B));

        static int Clamp0To255(int v) => v < 0 ? 0 : v > 255 ? 255 : v;
    }
    
    public static (int r, int g, int b) OracleIccRgb(Configuration config, byte c, byte m, byte y, byte k, bool invert)
    {
        var c0 = invert ? (255 - c) / 255.0 : c / 255.0;
        var m0 = invert ? (255 - m) / 255.0 : m / 255.0;
        var y0 = invert ? (255 - y) / 255.0 : y / 255.0;
        var k0 = invert ? (255 - k) / 255.0 : k / 255.0;

        var u = new Unicolour(config, new Channels(c0, m0, y0, k0));
        var rgb = u.Rgb.Byte255;

        return (Clamp0To255(rgb.R), Clamp0To255(rgb.G), Clamp0To255(rgb.B));

        static int Clamp0To255(int v) => v < 0 ? 0 : v > 255 ? 255 : v;
    }
}