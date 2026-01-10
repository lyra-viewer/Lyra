namespace Lyra.Imaging.Psd.Core.Decode.Pixel;

public readonly record struct PlaneImage(
    int Width,
    int Height,
    int BitsPerChannel,
    IReadOnlyList<Plane> Planes     // planar channel data
);

public readonly record struct Plane(
    PlaneRole Role,
    byte[] Data,                    // raw bytes for channel plane
    int BytesPerRow                 // helps for 1-bit, padding, etc.
);

public enum PlaneRole
{
    R,
    G,
    B,
    A,
    C,
    M,
    Y,
    K,
    Gray,
    Index,
    L,
    LabA,
    LabB,
    Mask,
    Spot
}