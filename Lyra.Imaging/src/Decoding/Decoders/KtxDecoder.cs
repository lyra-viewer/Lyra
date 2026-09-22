using Lyra.Common;
using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Structure;
using Lyra.Imaging.Decoding.Support;
using Lyra.ManagedCodecs.Texture;
using Lyra.ManagedCodecs.Texture.Ktx;
using SkiaSharp;

namespace Lyra.Imaging.Decoding.Decoders;

internal sealed class KtxDecoder : DecoderBase, IThumbnailDecoder
{
    public override bool CanDecode(ImageFormatType format) => format is ImageFormatType.Ktx;

    protected override void Decode(Composite composite, string path, CancellationToken ct)
    {
        var bytes = composite.ReadAllBytes(ct);

        // Basis Universal (ETC1S / UASTC) can't go through the managed reader; the native transcoder
        // decodes the base image straight to RGBA.
        if (BasisTranscoder.IsBasis(bytes))
        {
            DecodeBasis(composite, bytes);
            return;
        }

        var texture = ReadTexture(bytes);
        var surface = texture.Subresources[0]; // mip 0, face 0, layer 0

        TextureBitmap.PopulateMetadata(composite, texture);
        composite.Structure = KtxStructure.Describe(bytes, texture);

        ct.ThrowIfCancellationRequested();
        DecoderValidation.RequireSaneDimensions(nameof(KtxDecoder), surface.Width, surface.Height, TextureBitmap.BytesPerDecodedPixel(texture));

        composite.Content = TextureBitmap.DecodeToContent(texture, surface, composite, ct, flipVertical: texture.Origin == TextureOrigin.BottomLeft);
    }

    public SKBitmap? DecodeThumbnail(string path, int maxDimension, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var bytes = DecoderIO.ReadAllBytes(path, ct, out _);
        if (BasisTranscoder.IsBasis(bytes))
        {
            return ThumbnailScaler.ResizeToThumbnail(BasisTranscoder.Decode(bytes), maxDimension);
        }

        var texture = ReadTexture(bytes);
        var surface = TextureBitmap.SelectThumbnailSurface(texture, maxDimension);

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

        composite.Content = RasterContentBuilder.Build(bitmap, composite);
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

    private static SKBitmap DecodeToBitmap(TextureData texture, in Subresource surface, CancellationToken ct)
    {
        var bitmap = TextureBitmap.DecodeToBitmap(texture, surface, ct);

        // Decoders emit top-left RGBA; flip a bottom-up (OpenGL-convention) source into place.
        if (texture.Origin == TextureOrigin.BottomLeft)
        {
            TextureBitmap.FlipBitmapRows(bitmap);
        }

        return bitmap;
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