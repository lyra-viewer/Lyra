using Lyra.Common;
using Lyra.Common.SystemExtensions;
using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Structure;
using Lyra.Imaging.Decoding.Support;
using Lyra.ManagedCodecs.Texture;
using Lyra.ManagedCodecs.Texture.Dds;
using SkiaSharp;
using static System.Threading.Thread;

namespace Lyra.Imaging.Decoding.Decoders;

/// <summary>
/// Pipeline adapter for DirectDraw Surface (.dds) textures. Parses the container with
/// <see cref="DdsReader"/>, then decodes the base surface (mip 0, face 0, layer 0) to RGBA8 for
/// display. Thumbnails pick the smallest stored mip that still covers the target size, so perceptual
/// hashing never decodes the full-resolution surface.
/// </summary>
internal sealed class DdsDecoder : IImageDecoder, IThumbnailDecoder
{
    public bool CanDecode(ImageFormatType format) => format is ImageFormatType.Dds;

    public Task DecodeAsync(Composite composite, CancellationToken ct)
    {
        var path = composite.FileInfo.FullName;
        composite.DecoderName = nameof(DdsDecoder);
        Logger.Debug($"[DdsDecoder] [Thread: {CurrentThread.GetNameOrId()}] Decoding: {path}");

        ct.ThrowIfCancellationRequested();

        var bytes = File.ReadAllBytes(path);
        var texture = DdsReader.Read(bytes);
        var surface = texture.Subresources[0]; // mip 0, face 0, layer 0

        PopulateMetadata(composite, texture);
        composite.Structure = DdsStructure.Describe(bytes, texture);

        ct.ThrowIfCancellationRequested();
        DecoderValidation.RequireSaneDimensions(nameof(DdsDecoder), surface.Width, surface.Height);

        var bitmap = DecodeToBitmap(texture, surface);
        bitmap.SetImmutable();
        var image = SKImage.FromBitmap(bitmap);

        composite.Content = new RasterContent(bitmap, image);
        return Task.CompletedTask;
    }

    private static void PopulateMetadata(Composite composite, TextureData texture)
    {
        var info = TextureFormats.Info(texture.Format);

        composite.AddFormatSpecific("Format", texture.FormatName);
        composite.AddFormatSpecific("Has Alpha", info.HasAlpha ? "Yes" : "No");
        composite.AddFormatSpecific("Is Cubemap", texture.Kind == TextureKind.Cube ? "Yes" : "No");
        composite.AddFormatSpecific("Is Volume", texture.Kind == TextureKind.Volume ? "Yes" : "No");

        if (texture.Kind == TextureKind.Volume)
        {
            composite.AddFormatSpecific("Depth", $"{texture.Depth}");
        }

        composite.AddFormatSpecific("Mipmap Count", $"{texture.MipLevels}");
        composite.AddFormatSpecific("Bits/Pixel", $"{info.BitsPerPixel} bpp");
    }

    public SKBitmap? DecodeThumbnail(string path, int maxDimension, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var texture = DdsReader.Read(File.ReadAllBytes(path));
        var surface = SelectThumbnailSurface(texture, maxDimension);

        ct.ThrowIfCancellationRequested();

        var full = DecodeToBitmap(texture, surface);

        var longestSide = Math.Max(surface.Width, surface.Height);
        if (longestSide <= maxDimension)
        {
            return full;
        }

        var scale = (float)maxDimension / longestSide;
        var targetWidth = Math.Max(1, (int)MathF.Round(surface.Width * scale));
        var targetHeight = Math.Max(1, (int)MathF.Round(surface.Height * scale));

        using (full)
        {
            var info = new SKImageInfo(targetWidth, targetHeight, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            return full.Resize(info, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
        }
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

    private static SKBitmap DecodeToBitmap(TextureData texture, in Subresource surface)
    {
        var info = new SKImageInfo(surface.Width, surface.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var bitmap = new SKBitmap(info);

        if (texture.IsHdr)
        {
            var floats = new float[surface.Width * surface.Height * 4];
            texture.DecodeHdr(surface, floats);
            HdrToneMap.ToBitmap(floats, bitmap, CancellationToken.None, out _);
            return bitmap;
        }

        unsafe
        {
            var dst = new Span<byte>((void*)bitmap.GetPixels(), surface.Width * surface.Height * 4);
            texture.Decode(surface, dst);
        }

        return bitmap;
    }
}