namespace Lyra.Imaging.Psd.Core.Decode.ColorCalibration;

public static class LutBuilder
{
    public static RgbLuts BuildRgbCurves(int[] sumR, int[] cntR, int[] sumG, int[] cntG, int[] sumB, int[] cntB)
        => new(BuildCurve(sumR, cntR), BuildCurve(sumG, cntG), BuildCurve(sumB, cntB));

    private static byte[] BuildCurve(int[] sum, int[] cnt)
    {
        var total = 0;
        for (var i = 0; i < 256; i++)
            total += cnt[i];

        if (total == 0)
            return RgbLuts.Identity.R;

        var lut = new byte[256];

        for (var i = 0; i < 256; i++)
        {
            var c = cnt[i];
            if (c > 0)
            {
                var v = (sum[i] + (c / 2)) / c; // rounded average
                lut[i] = (byte)Math.Clamp(v, 0, 255);
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
                for (var j = last + 1; j < i; j++)
                {
                    var t = (j - last) / (float)(i - last);
                    var v = lut[last] + (lut[i] - lut[last]) * t;
                    lut[j] = (byte)(v + 0.5f);
                }
            }

            last = i;
        }

        if (last >= 0 && last < 255)
        {
            for (var j = last + 1; j < 256; j++)
                lut[j] = lut[last];
        }

        return lut;
    }
}