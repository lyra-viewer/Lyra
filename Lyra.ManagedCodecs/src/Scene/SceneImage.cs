namespace Lyra.ManagedCodecs.Scene;

public sealed record SceneImage(int Index, string? Name, string? MimeType, ImageSource Source);

public readonly record struct ByteRange(long Offset, long Length);

public abstract record ImageSource;

/// <summary>Bytes inside the model file itself.</summary>
public sealed record EmbeddedSource(ByteRange Range) : ImageSource;

/// <summary>Bytes carried as text in the model, such as a data URI, already decoded.</summary>
public sealed record InlineSource(ReadOnlyMemory<byte> Bytes) : ImageSource;

/// <summary>A file the model refers to: the image itself, or a buffer file holding it at <paramref name="Range"/>.</summary>
public sealed record ExternalSource(FileReference File, ByteRange? Range) : ImageSource;

/// <summary>An image the file describes but whose bytes cannot be found inside it.</summary>
public sealed record BrokenSource(string Reason) : ImageSource;

/// <summary>
/// A file as the model names it. Where it really is, if anywhere, is for the reader of the model to
/// find out: the paths are often absolute paths from the author's machine.
/// </summary>
/// <param name="StoredPath">The path to report, as the author's tool wrote it.</param>
/// <param name="Candidates">Every path the model gives for the file, in the order to try them.</param>
public sealed record FileReference(string StoredPath, IReadOnlyList<string> Candidates)
{
    public FileReference(string storedPath) : this(storedPath, [storedPath]) { }
}
