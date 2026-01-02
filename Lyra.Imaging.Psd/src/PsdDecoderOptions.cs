using SixLabors.ImageSharp.Formats;

namespace Lyra.Imaging.Psd;

public sealed class PsdDecoderOptions : ISpecializedDecoderOptions
{
    public DecoderOptions GeneralOptions { get; init; } = new();
}