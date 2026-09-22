using Lyra.Common;
using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Structure;
using Lyra.Imaging.Decoding.Support;
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

        TextureBitmap.PopulateMetadata(composite, texture);
        composite.Structure = DdsStructure.Describe(bytes, texture);

        ct.ThrowIfCancellationRequested();
        DecoderValidation.RequireSaneDimensions(nameof(DdsDecoder), surface.Width, surface.Height, TextureBitmap.BytesPerDecodedPixel(texture));

        composite.Content = TextureBitmap.DecodeToContent(texture, surface, composite, ct, flipVertical: false);
    }

    public SKBitmap? DecodeThumbnail(string path, int maxDimension, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var texture = DdsReader.Read(DecoderIO.ReadAllBytes(path, ct, out _));
        var surface = TextureBitmap.SelectThumbnailSurface(texture, maxDimension);

        ct.ThrowIfCancellationRequested();

        return ThumbnailScaler.ResizeToThumbnail(TextureBitmap.DecodeToBitmap(texture, surface, ct), maxDimension);
    }
}