using Lyra.Metadata.Exif;
using Lyra.Metadata.Layout;

namespace Lyra.Metadata.Containers;

public sealed class WebpContainer : IBufferedContainer
{
    private const int MaxChunks = 100_000;

    public static bool Recognizes(ReadOnlySpan<byte> head) => head.Length >= 12 && head.StartsWith("RIFF"u8) && head[8..12].SequenceEqual("WEBP"u8);

    public static ContainerContents? Read(ReadOnlySpan<byte> data)
    {
        if (!Recognizes(data))
            return null;

        var result = new ContainerContents();
        var chunks = 0;

        foreach (var chunk in RiffChunks.Children(RiffChunks.Form(data), start: 12, keepCutShort: true))
        {
            if (chunk.CutShort)
            {
                result.Damaged = true;
                break;
            }

            if (chunk.Is("EXIF"u8))
                result.Add(MetadataBlockKind.Exif, ExifPayload.WithoutPreamble(chunk.Body).ToArray());
            else if (chunk.Is("XMP "u8))
                result.Add(MetadataBlockKind.Xmp, chunk.Body.ToArray());
            else if (chunk.Is("ICCP"u8))
                result.Add(MetadataBlockKind.Icc, chunk.Body.ToArray());

            if (++chunks >= MaxChunks)
                break;
        }

        return result;
    }
}