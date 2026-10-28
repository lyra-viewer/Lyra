namespace Lyra.Imaging.Content.Variants;

/// <summary>
/// Supplies one rendition on demand. Implemented by decoders whose container holds more renditions
/// than are worth decoding up front.
/// </summary>
public interface IVariantProvider
{
    ICompositeContent Decode(int index, CancellationToken ct);
}