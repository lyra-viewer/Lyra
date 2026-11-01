using Lyra.Common;

namespace Lyra.FileLoader.Store;

public readonly record struct FileRecord(string Path, string Name, string Directory, long Size = 0, ulong PHash = 0, UInt128 ContentHash = default, int? GroupId = null, ImageFormatType Detected = ImageFormatType.Unknown)
{
    public string Extension => System.IO.Path.GetExtension(Path);

    public ImageFormatType Format => Detected != ImageFormatType.Unknown ? Detected : ImageFormat.GetImageFormat(Extension);

    public bool HasPHash => PHash != 0;

    public bool HasSize => Size != 0;

    public bool HasContentHash => ContentHash != UInt128.Zero;

    // Set when the file belongs to a duplicate cluster
    public bool HasGroup => GroupId.HasValue;
}