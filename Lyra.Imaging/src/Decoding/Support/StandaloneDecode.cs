using Lyra.Common;
using Lyra.Common.SystemExtensions;
using Lyra.Imaging.Content;
using Lyra.Imaging.Content.Status;
using Lyra.Imaging.Detection;
using Lyra.Imaging.Loading;

namespace Lyra.Imaging.Decoding.Support;

/// <summary>
/// Decodes an image outside the loader - bytes another file holds, or a file another file links to -
/// with the same decoder a file of its format gets. An image held in memory goes only to a decoder
/// that declares it reads from memory.
/// </summary>
internal static class StandaloneDecode
{
    public static bool CanDecodeFromMemory(ImageFormatType format) => DecoderManager.Can(format, DecoderCapabilities.ReadsFromMemory);

    public static Composite FromBytes(byte[] bytes, string name, string? mimeType, string? fileName, CancellationToken ct)
    {
        var format = ImageSignature.Detect(bytes, mimeType, fileName);

        if (format == ImageFormatType.Unknown)
            throw new LoadFailureException(LoadFailureKind.DecodeFailed, "The image's format is not recognised.");

        if (!CanDecodeFromMemory(format))
            throw new LoadFailureException(LoadFailureKind.DecodeFailed, $"{format.Description()} images held inside another file cannot be shown yet.");

        var composite = new Composite(new FileInfo(name), format, bytes);

        try
        {
            DecoderManager.GetDecoder(format).DecodeAsync(composite, ct).GetAwaiter().GetResult();

            if (composite.Content is null)
                throw new LoadFailureException(LoadFailureKind.DecodeFailed, "Nothing was decoded.");

            return composite;
        }
        catch
        {
            composite.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Decodes a file another file links to, as it would be opened directly. Refuses what cannot be
    /// part of another file: camera raw, and models. The caller owns the returned composite.
    /// </summary>
    public static Composite FromFile(string path, CancellationToken ct)
    {
        var composite = new Composite(new FileInfo(path));

        try
        {
            var named = composite.ImageFormatType;
            var head = Head(path);
            composite.Head = head;

            composite.ImageFormatType = FormatDetector.Resolve(named, head);

            if (named != ImageFormatType.Unknown && composite.ImageFormatType != named)
                composite.Warning = LoadWarning.Misnamed(composite.FileInfo.Extension, composite.ImageFormatType.Description());

            if (DecoderManager.Can(composite.ImageFormatType, DecoderCapabilities.OpensOnlyDirectly))
                throw new LoadFailureException(LoadFailureKind.DecodeFailed, $"A {composite.ImageFormatType.Description()} cannot be shown as part of another file.");

            DecoderManager.GetDecoder(composite.ImageFormatType).DecodeAsync(composite, ct).GetAwaiter().GetResult();
            AwaitBackgroundDecode(composite, ct);

            if (composite.Content is null)
                throw new LoadFailureException(LoadFailureKind.DecodeFailed, "Nothing was decoded.");

            return composite;
        }
        catch
        {
            AwaitBackgroundDecode(composite, CancellationToken.None);
            composite.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Some decoders, such as PSD, go on filling the content they returned from a background task.
    /// The content may be disposed as soon as it is handed on, so that waits until they are done.
    /// </summary>
    private static void AwaitBackgroundDecode(Composite composite, CancellationToken ct)
    {
        var task = composite.BackgroundDecodeTask;

        try
        {
            task.Wait(ct);
        }
        catch (OperationCanceledException)
        {
            // The task stops on the same token; it has to have stopped before the content can go.
            ((IAsyncResult)task).AsyncWaitHandle.WaitOne();
            throw;
        }
        catch (AggregateException) { }
    }

    private static byte[] Head(string path)
    {
        using var stream = File.OpenRead(path);
        var head = new byte[ImageSignature.HeadBytes];
        return head[..stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false)];
    }
}