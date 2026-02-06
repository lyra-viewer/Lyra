using Lyra.Imaging.Psd.Core.Common;

namespace Lyra.Imaging.Psd.Core.Decode.ColorProcessors;

public static class ColorModeProcessorFactory
{
    public static IColorModeProcessor GetProcessor(ColorMode mode) => mode switch
    {
        ColorMode.Rgb => new RgbProcessor(),
        ColorMode.Cmyk => new CmykProcessor(),
        ColorMode.Indexed => new IndexedProcessor(),
        // TODO:
        // ColorMode.Bitmap => new BitmapProcessor(),
        // ColorMode.Grayscale => new GrayscaleProcessor(),
        // ColorMode.Lab => new LabProcessor(),
        // ColorMode.Duotone => new DuotoneFallbackProcessor(),
        // ColorMode.Multichannel => new MultichannelFallbackProcessor(),
        ColorMode.Bitmap => throw new NotSupportedException("Bitmap (1bpp) is not processed via IColorModeProcessor (PlaneImage-based). Use Bitmap decode path."),
        _ => throw new NotSupportedException($"ColorMode {mode} not supported yet.")
    };
}