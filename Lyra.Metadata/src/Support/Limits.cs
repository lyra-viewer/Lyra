namespace Lyra.Metadata.Support;

internal static class Limits
{
    /// <summary>The start of a file, read in one piece while its container is walked.</summary>
    public const int HeadBytes = 64 * 1024 * 1024;

    /// <summary>A movie box (moov), read whole.</summary>
    public const int MovieBytes = 64 * 1024 * 1024;

    /// <summary>Metadata gathered from many pieces of one file: a JPEG's ICC segments, all a PNG inflates.</summary>
    public const int AssembledBytes = 64 * 1024 * 1024;

    /// <summary>One metadata block: a HEIF meta box or item, an XMP box, an MRW’s TIFF, an IPTC value, a JXL brob box.</summary>
    public const int BlockBytes = 16 * 1024 * 1024;

    /// <summary>An XMP, IPTC, Photoshop or ICC block in a TIFF's IFD0.</summary>
    public const int TiffBlockBytes = 8 * 1024 * 1024;

    /// <summary>One EXIF tag value or one ICC text.</summary>
    public const int ValueBytes = 64 * 1024;
}