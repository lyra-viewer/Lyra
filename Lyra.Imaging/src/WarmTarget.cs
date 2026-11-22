using Lyra.Common;

namespace Lyra.Imaging;

/// <summary>
/// A decoder whose first use in a run costs noticeably more than the ones after it, most worth
/// warming first. JPEG XL, JPEG 2000 and camera raw measured no such cost above noise.
/// </summary>
public enum WarmTarget
{
    Svg,
    Basis,
    Psd,
    Avif,
    Exr,
    Heic,
    Tiff,
    Hdr
}

public static class WarmTargets
{
    public static WarmTarget? For(ImageFormatType format, string path) => format switch
    {
        ImageFormatType.Svg => WarmTarget.Svg,
        ImageFormatType.Psd or ImageFormatType.Psb => WarmTarget.Psd,
        ImageFormatType.Heif => path.EndsWith(".avif", StringComparison.OrdinalIgnoreCase) ? WarmTarget.Avif : WarmTarget.Heic,
        ImageFormatType.Ktx => path.EndsWith(".ktx2", StringComparison.OrdinalIgnoreCase) ? WarmTarget.Basis : null,
        ImageFormatType.Exr => WarmTarget.Exr,
        ImageFormatType.Hdr => WarmTarget.Hdr,
        ImageFormatType.Tiff => WarmTarget.Tiff,
        _ => null
    };
}