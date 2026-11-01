using Lyra.Imaging.Decoding.Support;

namespace Lyra.Imaging.Decoding;

/// <summary>
/// The settings one decode runs under, read once when it starts, so a display or curve that
/// changes partway through cannot leave its parts disagreeing.
/// </summary>
/// <param name="PreviewWidth">The widest a preview of a large image may be.</param>
/// <param name="PreviewHeight">The tallest a preview of a large image may be.</param>
/// <param name="Curve">What scene-referred light is baked through when it cannot stay HDR.</param>
internal sealed record DecodeOptions(int PreviewWidth, int PreviewHeight, ToneCurve Curve)
{
    public static DecodeOptions Current()
    {
        var (width, height) = RasterContentBuilder.PreviewBounds();
        return new DecodeOptions(width, height, HdrDecodeSettings.Curve);
    }

    public (int Width, int Height) PreviewSize(int width, int height) =>
        RasterContentBuilder.PreviewSize(width, height, PreviewWidth, PreviewHeight);
}
