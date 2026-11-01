using Lyra.Imaging.Content.Status;
using Lyra.ManagedCodecs.Scene;

namespace Lyra.Imaging.Decoding.Decoders.Model;

/// <summary>Fetches the bytes of a model's texture, wherever the model keeps them.</summary>
internal static class ModelImageBytes
{
    private const int ReadChunk = 4 * 1024 * 1024;
    
    public static byte[]? Read(ModelTexture texture, string modelPath, CancellationToken ct)
    {
        if (Unavailable(texture) is { } unavailable)
            throw unavailable;

        return texture.Image.Source switch
        {
            EmbeddedSource embedded => ReadRange(modelPath, embedded.Range, ct),
            InlineSource inline => inline.Bytes.ToArray(),
            ExternalSource { Range: { } range } => ReadRange(texture.Source.ResolvedPath!, range, ct),
            ExternalSource => null,
            var other => throw new ArgumentOutOfRangeException(nameof(texture), other, null)
        };
    }

    /// <summary>
    /// Fills <paramref name="into"/> from the start of the texture, as far as the texture goes.
    /// Returns how many bytes were read, or null when the texture is missing or broken.
    /// </summary>
    public static int? ReadHead(ModelTexture texture, string modelPath, Span<byte> into, CancellationToken ct)
    {
        if (Unavailable(texture) is not null)
            return null;

        switch (texture.Image.Source)
        {
            case InlineSource inline:
                var bytes = inline.Bytes.Span[..Math.Min(inline.Bytes.Length, into.Length)];
                bytes.CopyTo(into);
                return bytes.Length;

            case EmbeddedSource embedded:
                return ReadInto(modelPath, Head(embedded.Range, into.Length), into, ct);

            case ExternalSource { Range: { } range }:
                return ReadInto(texture.Source.ResolvedPath!, Head(range, into.Length), into, ct);

            case ExternalSource:
                var path = texture.Source.ResolvedPath!;
                return ReadInto(path, new ByteRange(0, Math.Min(new FileInfo(path).Length, into.Length)), into, ct);

            default:
                return null;
        }
    }

    private static ByteRange Head(ByteRange range, int max) => range with { Length = Math.Min(range.Length, max) };

    /// <summary>Why a texture cannot be shown, when that is known without reading anything.</summary>
    public static LoadFailureException? Unavailable(ModelTexture texture) => texture.Image.Source switch
    {
        BrokenSource broken => new LoadFailureException(LoadFailureKind.DecodeFailed, broken.Reason),
        ExternalSource when texture.Source.ResolvedPath is null => new LoadFailureException(LoadFailureKind.NotFound, $"Missing: {texture.Source.StoredPath}"),
        _ => null
    };

    private static byte[] ReadRange(string path, ByteRange range, CancellationToken ct)
    {
        if (range.Length > Array.MaxLength)
            throw new LoadFailureException(LoadFailureKind.TooLarge, $"The image is {range.Length} bytes, too large to read.");

        var bytes = new byte[Math.Max(0, range.Length)];
        ReadInto(path, range, bytes, ct);
        return bytes;
    }

    /// <summary>A range of a file, checked against the file as it is now, which may have changed since the model was read.</summary>
    private static int ReadInto(string path, ByteRange range, Span<byte> into, CancellationToken ct)
    {
        using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.RandomAccess);

        var length = RandomAccess.GetLength(handle);
        if (range.Offset < 0 || range.Length < 0 || range.Offset > length || range.Length > length - range.Offset || range.Length > into.Length)
            throw new LoadFailureException(LoadFailureKind.DecodeFailed, $"The image reaches past the end of {Path.GetFileName(path)}.");

        var target = into[..(int)range.Length];
        for (var done = 0; done < target.Length; )
        {
            ct.ThrowIfCancellationRequested();

            var read = RandomAccess.Read(handle, target.Slice(done, Math.Min(ReadChunk, target.Length - done)), range.Offset + done);
            if (read == 0)
                throw new LoadFailureException(LoadFailureKind.DecodeFailed, $"{Path.GetFileName(path)} ended while the image was read.");

            done += read;
        }

        return target.Length;
    }
}