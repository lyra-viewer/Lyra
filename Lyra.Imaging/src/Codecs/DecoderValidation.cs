namespace Lyra.Imaging.Codecs;

internal static class DecoderValidation
{
    private const int MaxDimension = 1_048_575;

    public static void RequireSaneDimensions(string decoder, int width, int height, int bytesPerPixel = 4)
    {
        if (width <= 0 || height <= 0)
            throw new InvalidOperationException($"[{decoder}] Invalid dimensions: {width}x{height}.");

        if (width > MaxDimension || height > MaxDimension)
            throw new InvalidOperationException($"[{decoder}] Dimensions exceed limit ({MaxDimension}): {width}x{height}.");

        // checked() ensures it never silently wraps into a small allocation request
        _ = checked((long)width * height * bytesPerPixel);
    }

    public static void RequireValidStride(string decoder, int stride, int width, int bytesPerPixel = 4)
    {
        if (stride <= 0)
            throw new InvalidOperationException($"[{decoder}] Invalid stride: {stride}.");

        var minStride = checked(width * bytesPerPixel);
        if (stride < minStride)
            throw new InvalidOperationException($"[{decoder}] Stride {stride} is smaller than width×bpp ({minStride}).");
    }
}