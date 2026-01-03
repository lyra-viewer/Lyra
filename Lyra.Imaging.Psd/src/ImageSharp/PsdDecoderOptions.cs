using SixLabors.ImageSharp.Formats;

namespace Lyra.Imaging.Psd.ImageSharp;

public sealed class PsdDecoderOptions : ISpecializedDecoderOptions
{
    public DecoderOptions GeneralOptions { get; init; } = new();

    public int? PreviewMaxDimension { get; set; }
    public bool ProducePreview { get; set; } = false;
}