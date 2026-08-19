namespace Lyra.Imaging.Content;

public interface ICompositeContent : IDisposable
{
    /// <summary>
    /// Sampling has no meaning for this content: it re-renders at whatever zoom it is drawn at,
    /// so there is no filter choice to make and nothing to tell the user about.
    /// </summary>
    bool IsResolutionIndependent { get; }

    float? DecodedWidth { get; }
    float? DecodedHeight { get; }
    
    long ByteSize => (long)(DecodedWidth ?? 0f) * (long)(DecodedHeight ?? 0f) * 4;
}