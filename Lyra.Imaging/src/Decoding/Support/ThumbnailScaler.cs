using SkiaSharp;

namespace Lyra.Imaging.Decoding.Support;

internal static class ThumbnailScaler
{
    /// <summary>
    /// Returns <paramref name="full"/> as-is if its longest side already fits within
    /// <paramref name="maxDimension"/>, else a linearly-downscaled copy (disposing the original).
    /// The caller does the final downscale to the hash grid, so a single linear pass is enough.
    /// </summary>
    public static SKBitmap? ResizeToThumbnail(SKBitmap full, int maxDimension)
    {
        var longestSide = Math.Max(full.Width, full.Height);
        if (longestSide <= maxDimension)
        {
            return full;
        }

        var scale = (float)maxDimension / longestSide;
        var targetWidth = Math.Max(1, (int)MathF.Round(full.Width * scale));
        var targetHeight = Math.Max(1, (int)MathF.Round(full.Height * scale));

        using (full)
        {
            var info = new SKImageInfo(targetWidth, targetHeight, full.ColorType, full.AlphaType);
            return full.Resize(info, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
        }
    }
}
