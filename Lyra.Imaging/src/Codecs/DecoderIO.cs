namespace Lyra.Imaging.Codecs;

public static class DecoderIO
{
    private const int SequentialBuffer = 64 * 1024;
    private const int RandomBuffer = 16 * 1024;
    
    public static FileStream OpenSequentialRead(string path)
        => new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            SequentialBuffer,
            FileOptions.SequentialScan
        );

    public static FileStream OpenRandomAccessRead(string path)
        => new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            RandomBuffer,
            FileOptions.RandomAccess
        );
}