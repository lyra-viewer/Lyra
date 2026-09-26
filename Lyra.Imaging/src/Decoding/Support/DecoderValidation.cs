using Lyra.Imaging.Content;

namespace Lyra.Imaging.Decoding.Support;

internal static class DecoderValidation
{
    private const int MaxDimension = 1_048_575;

    public static void RequireSaneDimensions(int width, int height, int bytesPerPixel = 4)
    {
        if (width <= 0 || height <= 0)
            throw new InvalidOperationException($"Invalid dimensions: {width}x{height}");

        if (width > MaxDimension || height > MaxDimension)
            throw new LoadFailureException(LoadFailureKind.TooLarge, $"Dimensions exceed limit ({MaxDimension}): {width}x{height}");

        // checked() ensures it never silently wraps into a small allocation request
        _ = checked((long)width * height * bytesPerPixel);
    }

    public static void RequireAvailableMemory(long width, long height, int bytesPerPixel = 4) =>
        RequireAvailableMemory(width, height, bytesPerPixel, GC.GetGCMemoryInfo().TotalAvailableMemoryBytes);

    internal static void RequireAvailableMemory(long width, long height, int bytesPerPixel, long availableBytes)
    {
        if (availableBytes <= 0)
            return;

        // In double: two 32-bit dimensions and a pixel size can overflow a long.
        var bytes = (double)width * height * bytesPerPixel;
        if (bytes <= availableBytes)
            return;

        throw new LoadFailureException(LoadFailureKind.TooLarge, $"{width}x{height} would need {bytes / (1024 * 1024):0} MB decoded, more than the {availableBytes / (1024 * 1024)} MB this machine has");
    }

    public static void RequireValidStride(int stride, int width, int bytesPerPixel = 4)
    {
        if (stride <= 0)
            throw new InvalidOperationException($"Invalid stride: {stride}");

        var minStride = checked(width * bytesPerPixel);
        if (stride < minStride)
            throw new InvalidOperationException($"Stride {stride} is smaller than width×bpp ({minStride})");
    }
}