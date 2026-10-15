using Lyra.Imaging.Decoding.Support;
using Lyra.ManagedCodecs.Raster.Dng;

namespace Lyra.Imaging.Decoding.Decoders.Raw;

/// <summary>
/// Developed raw light with a DNG gain table map applied: the maker's local tone mapping, gains
/// that depend on where a pixel is and how bright it is.
/// </summary>
/// <param name="raw">Linear sRGB with the sensor's saturation at 1.0, oriented as shown.</param>
/// <param name="flip">LibRaw's orientation of <paramref name="raw"/>, to find each pixel in the sensor's frame.</param>
/// <param name="baselineGain">The DNG's baseline exposure, which the table's input is measured after.</param>
/// <param name="outputGain">Applied to the result, as the exposure of the stored light.</param>
internal sealed unsafe class GainMappedRows(LinearRows raw, DngGainTableMap map, int flip, float baselineGain, float outputGain)
    : LinearRows(raw.Width, raw.Height)
{
    private readonly record struct SensorPoint(double Across, double Down);

    protected override bool NeedsScratch => true;

    public override float* Row(int y, float* scratch)
    {
        var row = raw.Row(y, scratch);
        if (row != scratch)
            Buffer.MemoryCopy(row, scratch, (long)Width * 4 * sizeof(float), (long)Width * 4 * sizeof(float));

        for (var x = 0; x < Width; x++)
        {
            var p = scratch + x * 4;
            var gain = GainAt(p[0], p[1], p[2], x, y);
            p[0] *= gain;
            p[1] *= gain;
            p[2] *= gain;
        }

        return scratch;
    }

    public override void Rgb(long pixel, out float r, out float g, out float b)
    {
        raw.Rgb(pixel, out r, out g, out b);

        var gain = GainAt(r, g, b, (int)(pixel % Width), (int)(pixel / Width));
        r *= gain;
        g *= gain;
        b *= gain;
    }

    private float GainAt(float r, float g, float b, int x, int y)
    {
        var m = ColorMatrix.ToCurveSpace;
        var pr = (m[0] * r + m[1] * g + m[2] * b) * baselineGain;
        var pg = (m[3] * r + m[4] * g + m[5] * b) * baselineGain;
        var pb = (m[6] * r + m[7] * g + m[8] * b) * baselineGain;

        var at = SensorPosition((x + 0.5) / Width, (y + 0.5) / Height);
        return map.Gain(pr, pg, pb, at.Across, at.Down) * outputGain;
    }

    private SensorPoint SensorPosition(double u, double v)
    {
        var transposed = (flip & 4) != 0;
        var down = transposed ? u : v;
        var across = transposed ? v : u;

        if ((flip & 2) != 0)
            down = 1 - down;

        if ((flip & 1) != 0)
            across = 1 - across;

        return new SensorPoint(across, down);
    }
}
