namespace Lyra.Metadata.Tiff;

public enum TiffDirectoryKind
{
    /// <summary>One of the main chain: IFD0, IFD1 and on.</summary>
    Image,

    /// <summary>Pointed at by SubIFDs (330): a raw's sensor data, a reduced image.</summary>
    SubImage,

    /// <summary>Pointed at by ExifIFD (0x8769).</summary>
    Exif,

    /// <summary>Pointed at by GPSInfo (0x8825).</summary>
    Gps,

    /// <summary>Pointed at by InteroperabilityIFD (0xA005), from the EXIF directory.</summary>
    Interop
}