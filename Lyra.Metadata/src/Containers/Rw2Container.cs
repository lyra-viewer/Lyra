using Lyra.Metadata.Tiff;

namespace Lyra.Metadata.Containers;

/// <summary>A Panasonic RW2. Its own EXIF directory is a sparse copy; the full EXIF is in the JPEG it carries.</summary>
internal static class Rw2Container
{
    private const ushort JpgFromRaw = 0x002E;

    private const int JpegHeaderBytes = 256 * 1024;

    public static byte[]? CameraExif(Stream stream, TiffLayout layout)
    {
        if (layout is not { Version: TiffDirectoryReader.PanasonicRawVersion, Directories: [var ifd0, ..] } || ifd0.Find(JpgFromRaw) is not { } entry)
            return null;

        var head = entry with { ValueSize = Math.Min(entry.ValueSize, JpegHeaderBytes) };
        return TiffDirectoryReader.ReadBytes(stream, head, JpegHeaderBytes) is { } jpeg
            ? JpegContainer.Read(jpeg)?.Blocks.FirstOrDefault(block => block.Kind == MetadataBlockKind.Exif)?.Data
            : null;
    }
}