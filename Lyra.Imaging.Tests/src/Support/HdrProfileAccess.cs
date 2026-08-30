using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Support;

namespace Lyra.Imaging.Tests.Support;

internal static class HdrProfileAccess
{
    public static float MeasureWhitePoint(Span<float> rgba) => HdrToneMap.MeasureWhitePoint(rgba);
    
    public static ICompositeContent Build(Span<float> rgba, int width, int height, out bool isGrayscale) =>
        HdrImageBuilder.Build(rgba, width, height, new Composite(new FileInfo("profile.exr")), CancellationToken.None, out isGrayscale);
}