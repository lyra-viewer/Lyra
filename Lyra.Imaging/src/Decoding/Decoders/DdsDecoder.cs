using Lyra.Common;
using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Structure;
using Lyra.Imaging.Decoding.Support;
using Lyra.ManagedCodecs.Texture;
using Lyra.ManagedCodecs.Texture.Dds;
using SkiaSharp;

namespace Lyra.Imaging.Decoding.Decoders;

/// <summary>
/// Pipeline adapter for DirectDraw Surface (.dds) textures. Parses the container with
/// <see cref="DdsReader"/>, then decodes the base surface (mip 0, face 0, layer 0) to RGBA8 for
/// display. Thumbnails pick the smallest stored mip that still covers the target size, so perceptual
/// hashing never decodes the full-resolution surface.
/// </summary>
internal sealed class DdsDecoder : DecoderBase, IThumbnailDecoder
{
    public override bool CanDecode(ImageFormatType format) => format is ImageFormatType.Dds;

    protected override void Decode(Composite composite, string path, CancellationToken ct)
    {
        var bytes = composite.ReadAllBytes(ct);
        var texture = DdsReader.Read(bytes);
        var surface = texture.Subresources[0]; // mip 0, face 0, layer 0

        PopulateMetadata(composite, texture);
        composite.Structure = DdsStructure.Describe(bytes, texture);

        ct.ThrowIfCancellationRequested();
        DecoderValidation.RequireSaneDimensions(nameof(DdsDecoder), surface.Width, surface.Height, TextureBitmap.BytesPerDecodedPixel(texture));

        composite.Content = TextureBitmap.DecodeToContent(texture, surface, composite, ct, flipVertical: false);
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

        var texture = DdsReader.Read(DecoderIO.ReadAllBytes(path, ct, out _));
        var surface = SelectThumbnailSurface(texture, maxDimension);

        ct.ThrowIfCancellationRequested();

        return ThumbnailScaler.ResizeToThumbnail(TextureBitmap.DecodeToBitmap(texture, surface, ct), maxDimension);
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
}