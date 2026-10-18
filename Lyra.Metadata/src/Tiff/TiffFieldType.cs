namespace Lyra.Metadata.Tiff;

public enum TiffFieldType : ushort
{
    Byte      = 1,
    Ascii     = 2,
    Short     = 3,
    Long      = 4,
    Rational  = 5,
    SByte     = 6,
    Undefined = 7,
    SShort    = 8,
    SLong     = 9,
    SRational = 10,
    Float     = 11,
    Double    = 12,
    Ifd       = 13, // A LONG that points at a directory.
    Long8     = 16, // BigTIFF's 64-bit LONG.
    SLong8    = 17,
    Ifd8      = 18  // BigTIFF's 64-bit IFD.
}