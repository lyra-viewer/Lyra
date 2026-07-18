using Lyra.Common;
using Lyra.Common.SystemExtensions;
using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Structure;
using Lyra.Imaging.Decoding.Support;
using Lyra.ManagedCodecs.Texture;
using Lyra.ManagedCodecs.Texture.Ktx;
using SkiaSharp;
using static System.Threading.Thread;

namespace Lyra.Imaging.Decoding.Decoders;

internal sealed class KtxDecoder : IImageDecoder, IThumbnailDecoder
{
    public bool CanDecode(ImageFormatType format) => format is ImageFormatType.Ktx;

    public Task DecodeAsync(Composite composite, CancellationToken ct)
    {
        var path = composite.FileInfo.FullName;
        composite.DecoderName = nameof(KtxDecoder);
        Logger.Debug($"[KtxDecoder] [Thread: {CurrentThread.GetNameOrId()}] Decoding: {path}");

        ct.ThrowIfCancellationRequested();

        var bytes = File.ReadAllBytes(path);

        // Basis Universal (ETC1S / UASTC) can't go through the managed reader; the native transcoder
        // decodes the base image straight to RGBA.
        if (BasisTranscoder.IsBasis(bytes))
        {
            DecodeBasis(composite, bytes);
            return Task.CompletedTask;
        }

        var texture = ReadTexture(bytes);
        var surface = texture.Subresources[0]; // mip 0, face 0, layer 0

        PopulateMetadata(composite, texture);
        composite.Structure = KtxStructure.Describe(bytes, texture);

        ct.ThrowIfCancellationRequested();
        DecoderValidation.RequireSaneDimensions(nameof(KtxDecoder), surface.Width, surface.Height);

        var bitmap = DecodeToBitmap(texture, surface, ct);
        bitmap.SetImmutable();
        var image = SKImage.FromBitmap(bitmap);

        composite.Content = new RasterContent(bitmap, image);
        return Task.CompletedTask;
    }

    public SKBitmap? DecodeThumbnail(string path, int maxDimension, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var bytes = File.ReadAllBytes(path);
        if (BasisTranscoder.IsBasis(bytes))
        {
            return ThumbnailScaler.ResizeToThumbnail(BasisTranscoder.Decode(bytes), maxDimension);
        }

        var texture = ReadTexture(bytes);
        var surface = SelectThumbnailSurface(texture, maxDimension);

        ct.ThrowIfCancellationRequested();

        return ThumbnailScaler.ResizeToThumbnail(DecodeToBitmap(texture, surface, ct), maxDimension);
    }

    /// <summary>Decodes a Basis Universal KTX2's base image via the native transcoder and fills the composite.</summary>
    private static void DecodeBasis(Composite composite, byte[] bytes)
    {
        var bitmap = BasisTranscoder.Decode(bytes);
        DecoderValidation.RequireSaneDimensions(nameof(KtxDecoder), bitmap.Width, bitmap.Height);

        composite.AddFormatSpecific("Format", BasisTranscoder.CodecName(bytes));
        composite.AddFormatSpecific("Has Alpha", HasTranslucentPixels(bitmap) ? "Yes" : "No");

        bitmap.SetImmutable();
        var image = SKImage.FromBitmap(bitmap);
        composite.Content = new RasterContent(bitmap, image);
    }

    /// <summary>Dispatches to the KTX 1.x or KTX 2.0 reader by the file's leading identifier bytes.</summary>
    private static TextureData ReadTexture(byte[] bytes)
    {
        if (KtxShared.IsKtx2(bytes))
            return Ktx2Reader.Read(bytes);

        if (KtxShared.IsKtx1(bytes))
            return KtxReader.Read(bytes);

        throw new InvalidDataException("KTX: missing or unrecognized Khronos Texture identifier.");
    }

    private static void PopulateMetadata(Composite composite, TextureData texture)
    {
        var info = TextureFormats.Info(texture.Format);

        composite.AddFormatSpecific("Format", texture.FormatName);
        composite.AddFormatSpecific("Has Alpha", info.HasAlpha ? "Yes" : "No");
        composite.AddFormatSpecific("Is Cubemap", texture.Kind == TextureKind.Cube ? "Yes" : "No");
        composite.AddFormatSpecific("Is Volume", texture.Kind == TextureKind.Volume ? "Yes" : "No");

        if (texture.Kind == TextureKind.Volume) 
            composite.AddFormatSpecific("Depth", $"{texture.Depth}");

        composite.AddFormatSpecific("Mipmap Count", $"{texture.MipLevels}");
        composite.AddFormatSpecific("Bits/Pixel", $"{info.BitsPerPixel} bpp");
    }

    /// <summary>Smallest stored mip (of face 0, layer 0) whose longest side still covers the target.</summary>
    private static Subresource SelectThumbnailSurface(TextureData texture, int maxDimension)
    {
        var chosen = texture.Subresources[0];
        foreach (var sr in texture.Subresources)
        {
            if (sr.ArrayLayer != 0 || sr.Face != 0)
                continue;

            if (Math.Max(sr.Width, sr.Height) >= maxDimension && sr.MipLevel > chosen.MipLevel)
                chosen = sr;
        }

        return chosen;
    }

    private static SKBitmap DecodeToBitmap(TextureData texture, in Subresource surface, CancellationToken ct)
    {
        var bitmap = TextureBitmap.DecodeToBitmap(texture, surface, ct);

        // Decoders emit top-left RGBA; flip a bottom-up (OpenGL-convention) source into place.
        if (texture.Origin == TextureOrigin.BottomLeft)
        {
            FlipVertical(bitmap);
        }

        return bitmap;
    }

    private static unsafe void FlipVertical(SKBitmap bitmap)
    {
        var height = bitmap.Height;
        var rowBytes = bitmap.RowBytes;
        var pixels = (byte*)bitmap.GetPixels();

        var row = new byte[rowBytes];
        fixed (byte* tmp = row)
        {
            for (var y = 0; y < height / 2; y++)
            {
                var top = pixels + (long)y * rowBytes;
                var bottom = pixels + (long)(height - 1 - y) * rowBytes;

                Buffer.MemoryCopy(top, tmp, rowBytes, rowBytes);
                Buffer.MemoryCopy(bottom, top, rowBytes, rowBytes);
                Buffer.MemoryCopy(tmp, bottom, rowBytes, rowBytes);
            }
        }
    }

    private static unsafe bool HasTranslucentPixels(SKBitmap bitmap)
    {
        var width = bitmap.Width;
        var height = bitmap.Height;
        var rowBytes = bitmap.RowBytes;
        var pixels = (byte*)bitmap.GetPixels();

        for (var y = 0; y < height; y++)
        {
            var row = pixels + (long)y * rowBytes;
            for (var x = 0; x < width; x++)
            {
                if (row[x * 4 + 3] != 255)
                    return true;
            }
        }

        return false;
    }
}