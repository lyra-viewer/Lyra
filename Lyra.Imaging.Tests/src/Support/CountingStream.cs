namespace Lyra.Imaging.Tests.Support;

/// <summary>An in-memory stream that counts the bytes read from it, to show what a reader skipped.</summary>
internal sealed class CountingStream(byte[] data) : MemoryStream(data, writable: false)
{
    public long BytesRead { get; private set; }

    /// <summary>How often the length was asked for: a round trip each on a network share.</summary>
    public int LengthQueries { get; private set; }

    public override long Length
    {
        get
        {
            LengthQueries++;
            return base.Length;
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = base.Read(buffer, offset, count);
        BytesRead += read;
        return read;
    }

    public override int ReadByte()
    {
        var value = base.ReadByte();
        if (value >= 0)
            BytesRead++;

        return value;
    }
}
