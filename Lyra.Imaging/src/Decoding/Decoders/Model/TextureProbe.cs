using System.Buffers;
using Lyra.Common;
using Lyra.Imaging.Detection;
using Lyra.ManagedCodecs.Scene;
using SkiaSharp;

namespace Lyra.Imaging.Decoding.Decoders.Model;

/// <summary>Describes a model's textures from their headers, so they are listed with their size before they are decoded.</summary>
internal static class TextureProbe
{
    private const int HeadBytes = 64 * 1024;

    public static ModelTexture Probed(ModelTexture texture, string modelPath, CancellationToken ct)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(HeadBytes);

        try
        {
            int? read;
            try
            {
                read = ModelImageBytes.ReadHead(texture, modelPath, buffer.AsSpan(0, HeadBytes), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
            {
                return texture;
            }

            if (read is not { } length)
                return texture;

            var head = buffer.AsSpan(0, length);

            var format = ImageSignature.Detect(head, texture.Image.MimeType, (texture.Image.Source as ExternalSource)?.File.StoredPath ?? texture.Image.Name);
            if (format == ImageFormatType.Unknown)
                return texture;

            using var data = SKData.CreateCopy(head);
            using var codec = SKCodec.Create(data);

            return texture with { Variant = ModelTextureList.Described(texture, codec?.Info.Width ?? 0, codec?.Info.Height ?? 0, format) };
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}