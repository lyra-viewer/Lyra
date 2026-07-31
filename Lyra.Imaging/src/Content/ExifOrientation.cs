using System.ComponentModel;

namespace Lyra.Imaging.Content;

/// <summary>
/// EXIF orientation (IFD0 tag 0x0112): how stored pixels relate to the upright image.
/// The numeric values are the EXIF ones, and deliberately match SkiaSharp's SKEncodedOrigin.
/// </summary>
public enum ExifOrientation
{
    Unknown = 0,
    Normal = 1,

    [Description("Mirrored")]
    MirrorHorizontal = 2,

    [Description("180°")]
    Rotate180 = 3,

    [Description("Flipped")]
    MirrorVertical = 4,

    [Description("Mirrored + 270° CW")]
    MirrorHorizontalRotate270Cw = 5,

    [Description("90° CW")]
    Rotate90Cw = 6,

    [Description("Mirrored + 90° CW")]
    MirrorHorizontalRotate90Cw = 7,

    [Description("270° CW")]
    Rotate270Cw = 8
}