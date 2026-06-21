using System.Buffers.Binary;
using Lyra.Imaging.Interop;
using Lyra.ManagedCodecs.Texture.Ktx;
using SkiaSharp;

namespace Lyra.Imaging.Decoding.Support;

/// <summary>
/// Decodes Basis Universal images (ETC1S / UASTC) carried in KTX2 containers via the native
/// libbasis_native transcoder. The managed <see cref="Ktx2Reader"/> can't decode these (the data is a
/// proprietary supercompressed block stream), so the container is handed to Binomial's transcoder,
/// which emits straight 8-bit RGBA for one image (mip level / array layer / cubemap face).
/// </summary>
internal static class BasisTranscoder
{
    private const int VkFormatOffset = 12;          // KTX2: vkFormat follows the 12-byte identifier
    private const int PixelDepthOffset = 28;        // KTX2: pixelDepth field
    private const int SupercompressionOffset = 44;  // KTX2: supercompressionScheme field

    /// <summary>True if <paramref name="bytes"/> is a KTX2 file holding Basis data (vkFormat UNDEFINED).</summary>
    public static bool IsBasis(ReadOnlySpan<byte> bytes)
        => KtxShared.IsKtx2(bytes)
           && bytes.Length >= VkFormatOffset + 4
           && BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(VkFormatOffset, 4)) == 0;

    /// <summary>
    /// The Basis sub-codec, inferred from the supercompression scheme: BasisLZ (scheme 1) is always
    /// ETC1S; anything else with vkFormat UNDEFINED is UASTC. Used only for the inspector label.
    /// </summary>
    public static string CodecName(ReadOnlySpan<byte> bytes)
    {
        var scheme = bytes.Length >= SupercompressionOffset + 4 ? BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(SupercompressionOffset, 4)) : 0;
        return scheme == 1 ? "Basis Universal (ETC1S)" : "Basis Universal (UASTC)";
    }

    /// <summary>
    /// Transcodes one image to an RGBA8 <see cref="SKBitmap"/> (top-left origin, unpremultiplied alpha).
    /// Throws <see cref="InvalidOperationException"/> with the native transcoder's message on failure.
    /// </summary>
    public static SKBitmap Decode(byte[] bytes, int level = 0, int layer = 0, int face = 0)
    {
        // Basis is a 2D-only codec: ETC1S/UASTC are 2D block formats and KTX2 requires pixelDepth==0.
        // The transcoder would reject a volume with a generic "init failed", so name the real reason.
        var depth = bytes.Length >= PixelDepthOffset + 4 ? BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(PixelDepthOffset, 4)) : 0;
        if (depth > 0)
        {
            throw new InvalidOperationException($"[BasisTranscoder] Basis Universal does not support 3D textures (pixelDepth={depth}).");
        }

        if (!BasisNative.basis_decode_ktx2_rgba(bytes, bytes.Length, level, layer, face, out var ptr, out var width, out var height) || ptr == IntPtr.Zero)
        {
            var error = NativeErrors.GetUtf8ZOrAnsiZ(BasisNative.get_last_basis_error());
            throw new InvalidOperationException($"[BasisTranscoder] Failed to transcode Basis KTX2. {error}");
        }

        try
        {
            var byteCount = checked(width * height * 4);
            var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            var bitmap = new SKBitmap(info);

            unsafe
            {
                var src = new Span<byte>((void*)ptr, byteCount);
                var dst = new Span<byte>((void*)bitmap.GetPixels(), byteCount);
                src.CopyTo(dst);
            }

            return bitmap;
        }
        finally
        {
            BasisNative.basis_free_pixels(ptr);
        }
    }
}