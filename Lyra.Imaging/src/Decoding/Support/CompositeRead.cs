using Lyra.Imaging.Content;

namespace Lyra.Imaging.Decoding.Support;

internal static class CompositeRead
{
    public static byte[] ReadAllBytes(this Composite composite, CancellationToken ct)
    {
        if (composite.Payload is { } payload)
        {
            composite.CompleteTransfer(payload.Length, 0);
            return payload;
        }

        var data = DecoderIO.ReadAllBytes(composite.FileInfo.FullName, ct, out var elapsedMs, composite.ReportTransferred);

        composite.CompleteTransfer(data.Length, elapsedMs);

        return data;
    }

    public static Stream OpenSequentialRead(this Composite composite) =>
        composite.Payload is { } payload
            ? new MemoryStream(payload, writable: false)
            : DecoderIO.OpenSequentialRead(composite.FileInfo.FullName);
}
