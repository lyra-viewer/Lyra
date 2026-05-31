namespace Lyra.FileLoader.Store;

public readonly record struct FileRecord(string Path, string Name, string Directory, long Size = 0, ulong PHash = 0, UInt128 ContentHash = default)
{
    public string Extension => System.IO.Path.GetExtension(Path);

    public bool HasPHash => PHash != 0;

    public bool HasSize => Size != 0;

    public bool HasContentHash => ContentHash != UInt128.Zero;
}