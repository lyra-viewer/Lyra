namespace Lyra.Imaging.Tests.Support;

/// <summary>A file on disk for the duration of a test, deleted when the test is done with it.</summary>
internal sealed class TempFile : IDisposable
{
    public string Path { get; }

    public TempFile(byte[] content)
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lyra-test-{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(Path, content);
    }

    /// <summary>Deterministic filler, so a mis-sized or mis-offset read shows up as wrong content.</summary>
    public static byte[] Pattern(int size)
    {
        var bytes = new byte[size];
        for (var i = 0; i < size; i++)
            bytes[i] = (byte)(i * 31 + (i >> 8));
        
        return bytes;
    }

    public void Dispose()
    {
        try
        {
            File.Delete(Path);
        }
        catch (IOException)
        {
            // A leftover temp file is not worth failing a test over.
        }
    }
}
