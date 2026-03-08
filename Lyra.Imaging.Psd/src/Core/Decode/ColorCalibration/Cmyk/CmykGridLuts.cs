namespace Lyra.Imaging.Psd.Core.Decode.ColorCalibration.Cmyk;

public sealed class CmykGridLut
{
    public static CmykGridLut Identity { get; } = BuildIdentityLut();

    public int GridSize { get; }
    public bool InvertInput { get; }
    public byte[] SamplesRgb { get; }
    public bool IsIdentity { get; }

    public CmykGridLut(int gridSize, bool invertInput, byte[] samplesRgb, bool isIdentity = false)
    {
        if (gridSize < 2)
            throw new ArgumentOutOfRangeException(nameof(gridSize), "GridSize must be >= 2.");

        var expected = checked(gridSize * gridSize * gridSize * gridSize * 3);
        if (samplesRgb.Length != expected)
            throw new ArgumentException($"Expected {expected} bytes for a {gridSize}^4 RGB cube.", nameof(samplesRgb));

        GridSize = gridSize;
        InvertInput = invertInput;
        SamplesRgb = samplesRgb;
        IsIdentity = isIdentity;
    }

    /// <summary>
    /// Builds a 2^4 identity LUT using the standard subtractive CMYK->RGB model.
    /// PSD convention: 0 = full ink, 255 = no ink.
    /// R = (C/255) * (K/255) * 255  (since C=0 means full cyan -> no red contribution).
    ///
    /// This means the grid sample points are at {0, 255} for each channel.
    /// </summary>
    private static CmykGridLut BuildIdentityLut()
    {
        const int gridSize = 2;
        var samples = new byte[gridSize * gridSize * gridSize * gridSize * 3];

        for (var ic = 0; ic < gridSize; ic++)
        for (var im = 0; im < gridSize; im++)
        for (var iy = 0; iy < gridSize; iy++)
        for (var ik = 0; ik < gridSize; ik++)
        {
            // Grid index 0 -> byte value 0 (full ink), index 1 -> byte value 255 (no ink).
            int cVal = ic * 255;
            int mVal = im * 255;
            int yVal = iy * 255;
            int kVal = ik * 255;

            // PSD subtractive: channel value IS the "remaining light" fraction.
            // R = cVal * kVal / 255
            byte r = (byte)((cVal * kVal + 127) / 255);
            byte g = (byte)((mVal * kVal + 127) / 255);
            byte b = (byte)((yVal * kVal + 127) / 255);

            int flat = ((ic * gridSize + im) * gridSize + iy) * gridSize + ik;
            samples[flat * 3 + 0] = r;
            samples[flat * 3 + 1] = g;
            samples[flat * 3 + 2] = b;
        }

        return new CmykGridLut(gridSize, invertInput: false, samples, isIdentity: true);
    }
}