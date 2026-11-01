using Lyra.Imaging.Content.Status;
using Lyra.Imaging.Decoding.Support;
using Lyra.Imaging.Detection;

namespace Lyra.Imaging.Decoding;

/// <summary>
/// Where an image's bytes come from: a file of its own, or bytes another file held. A decoder that
/// can only read a file asks for one with <see cref="RequireFile"/>, which refuses bytes.
/// </summary>
internal sealed class DecodeSource
{
    private readonly string? _file;
    private readonly byte[]? _bytes;
    private byte[]? _head;

    private DecodeSource(string label, string? file, byte[]? bytes, long? length)
    {
        Label = label;
        _file = file;
        _bytes = bytes;
        Length = length;
    }

    /// <param name="head">The file's first bytes when they were already read, as format detection does.</param>
    public static DecodeSource File(string path, long? length, byte[]? head = null) => new(path, path, null, length) { _head = head };

    public static DecodeSource Memory(string label, byte[] bytes) => new(label, null, bytes, bytes.LongLength);

    /// <summary>The file's full path, or the name an image held in memory goes by. For messages.</summary>
    public string Label { get; }

    public long? Length { get; }

    public bool InMemory => _bytes is not null;

    /// <summary>
    /// The first <see cref="ImageSignature.HeadBytes"/> bytes, or all of a shorter source: read once,
    /// or kept from format detection, so a header can be read without opening the file again.
    /// </summary>
    public ReadOnlySpan<byte> Head => _head ??= ReadHead();

    public Stream OpenHead() => new MemoryStream(_head ??= ReadHead(), writable: false);

    private byte[] ReadHead()
    {
        if (_bytes is { } bytes)
            return bytes[..Math.Min(bytes.Length, ImageSignature.HeadBytes)];

        using var stream = OpenSequential();
        var head = new byte[ImageSignature.HeadBytes];
        var read = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);

        return read == head.Length ? head : head[..read];
    }

    public string RequireFile() => _file ?? throw new LoadFailureException(LoadFailureKind.DecodeFailed,
        $"{Path.GetFileName(Label)} is held inside another file, and its format can only be read from a file of its own.");

    public Stream OpenSequential() =>
        _bytes is { } bytes ? new MemoryStream(bytes, writable: false) : DecoderIO.OpenSequentialRead(_file!);

    public Stream OpenRandomAccess() =>
        _bytes is { } bytes ? new MemoryStream(bytes, writable: false) : DecoderIO.OpenRandomAccessRead(_file!);

    /// <summary>For a reader that keeps the source open while the image is shown, such as an animation.</summary>
    public Stream OpenHeld() =>
        _bytes is { } bytes ? new MemoryStream(bytes, writable: false) : DecoderIO.OpenHeldRead(_file!);

    public byte[] ReadAllBytes(CancellationToken ct, out double elapsedMs, Action<long>? onProgress = null)
    {
        if (_bytes is { } bytes)
        {
            elapsedMs = 0;
            return bytes;
        }

        return DecoderIO.ReadAllBytes(_file!, ct, out elapsedMs, onProgress);
    }
}