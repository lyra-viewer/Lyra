namespace Lyra.Imaging.Psd.Core.Decode.ColorCalibration;

public static class LutBuilder
{
    public static RgbLuts BuildRgbCurves(int[] sumR, int[] cntR, int[] sumG, int[] cntG, int[] sumB, int[] cntB)
        => new(BuildCurve(sumR, cntR), BuildCurve(sumG, cntG), BuildCurve(sumB, cntB));

    public static byte[] BuildCurve(int[] sum, int[] cnt)
    {
        var lut = new byte[256];
        for (var i = 0; i < 256; i++)
        {
            if (cnt[i] > 0)
            {
                var v = sum[i] / cnt[i];
                lut[i] = (byte)Math.Clamp(v, 0, 255);
            }
            else
            {
                lut[i] = 0;
            }
        }

        var last = -1;
        for (var i = 0; i < 256; i++)
        {
            if (cnt[i] <= 0)
                continue;

            if (last < 0)
            {
                for (var j = 0; j < i; j++)
                    lut[j] = lut[i];
            }
            else
            {
                int a = last, b = i;
                for (var j = a + 1; j < b; j++)
                {
                    var t = (j - a) / (float)(b - a);
                    var v = lut[a] + (lut[b] - lut[a]) * t;
                    lut[j] = (byte)(v + 0.5f);
                }
            }

            last = i;
        }

        if (last >= 0 && last < 255)
            for (var j = last + 1; j < 256; j++)
                lut[j] = lut[last];

        if (last < 0)
            for (var i = 0; i < 256; i++)
                lut[i] = (byte)i;

        return lut;
    }
}