namespace Lyra.Imaging.Content;

public interface ICompositeContent : IDisposable
{
    CompositeContentKind Kind { get; }
    
    float? DecodedWidth { get; }
    float? DecodedHeight { get; }
}

public enum CompositeContentKind
{
    None,
    Raster,
    Vector,
    RasterLarge
}