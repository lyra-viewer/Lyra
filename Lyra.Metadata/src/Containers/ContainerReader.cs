using Lyra.Metadata.Support;

namespace Lyra.Metadata.Containers;

public static class ContainerReader
{
    private const int SniffBytes = 64;

    private const int InitialHeadBytes = 256 * 1024;

    private delegate bool Recognizer(ReadOnlySpan<byte> head);

    private delegate ContainerContents? BufferReader(ReadOnlySpan<byte> data);

    private sealed record Format(Recognizer Recognizes, Func<Stream, ContainerContents?> Read);

    private static readonly Format[] Formats =
    [
        Streamed<HeifContainer>(),
        Streamed<QuickTimeContainer>(),
        Buffered<JpegContainer>(),
        Buffered<PngContainer>(),
        Buffered<WebpContainer>(),
        Buffered<GifContainer>(),
        Buffered<RafContainer>(),
        Streamed<MrwContainer>(),
        Buffered<BmpContainer>(),
        Buffered<IcoContainer>(),
        Buffered<JxlContainer>(),
        Buffered<Jp2Container>(),
        Buffered<TgaContainer>()
    ];

    private static Format Streamed<T>() where T : IStreamedContainer => new(T.Recognizes, T.Read);

    private static Format Buffered<T>() where T : IBufferedContainer => new(T.Recognizes, stream => ReadBuffered(stream, T.Read));
    
    public static ContainerContents? Read(Stream stream)
    {
        if (!stream.CanSeek)
            return null;

        using var _ = stream.KeepPosition();
        var sniff = new byte[(int)Math.Min(stream.Length, SniffBytes)];
        if (!stream.ReadAt(0, sniff))
            return null;

        foreach (var format in Formats)
            if (format.Recognizes(sniff))
                return format.Read(stream);

        return null;
    }
    
    private static ContainerContents? ReadBuffered(Stream stream, BufferReader read)
    {
        using var _ = stream.KeepPosition();
        var limit = stream is MemoryStream ? (int)Math.Min(stream.Length, int.MaxValue) : InitialHeadBytes;
        while (true)
        {
            var head = Head(stream, limit);
            var container = read(head);

            if (container is not { Damaged: true } || head.Length >= stream.Length || limit >= Limits.HeadBytes)
                return container;

            limit = (int)Math.Min((long)limit * 4, Limits.HeadBytes);
        }
    }

    private static ReadOnlySpan<byte> Head(Stream stream, int limit)
    {
        var length = (int)Math.Min(stream.Length, limit);

        if (stream is MemoryStream memory && memory.TryGetBuffer(out var buffer))
            return buffer.AsSpan(0, Math.Min(length, buffer.Count));

        var head = new byte[length];
        stream.Position = 0;
        var read = stream.ReadAtLeast(head, length, throwOnEndOfStream: false);
        return head.AsSpan(0, read);
    }
}