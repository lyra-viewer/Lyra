using Lyra.Imaging.Content;

namespace Lyra.Imaging.Decoding.Support;

internal readonly record struct PixelFacts(bool IsGrayscale, string? DynamicRange = null)
{
    public void Describe(DecodeContext context)
    {
        if (DynamicRange is not null)
            context.AddFormatSpecific("Dynamic Range", DynamicRange);

        context.AddFormatSpecific("Grayscale", IsGrayscale);
    }
}