using Lyra.Imaging.Content;

namespace Lyra.Imaging.Decoding.Support;

internal readonly record struct PixelFacts(bool IsGrayscale, string? DynamicRange = null)
{
    public void Describe(Composite composite)
    {
        if (DynamicRange is not null)
            composite.AddFormatSpecific("Dynamic Range", DynamicRange);

        composite.AddFormatSpecific("GrayScale", IsGrayscale);
    }
}