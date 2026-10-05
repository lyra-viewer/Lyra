using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Lyra.Imaging.Decoding.Support;
using SkiaSharp;
using static Lyra.Imaging.Interop.HeifNative;

namespace Lyra.Imaging.Interop;

internal sealed class HeifImageHandle(IntPtr handle, HeifFile file) : IDisposable
{
    private IntPtr _handle = handle;

    public int Width => heif_image_handle_get_width(_handle);

    public int Height => heif_image_handle_get_height(_handle);

    public bool HasAlphaChannel => heif_image_handle_has_alpha_channel(_handle) != 0;

    public bool HasDepthImage => heif_image_handle_has_depth_image(_handle) != 0;

    public uint[] ThumbnailIds()
    {
        var count = heif_image_handle_get_number_of_thumbnails(_handle);
        if (count <= 0)
            return [];

        var ids = new uint[count];
        var listed = heif_image_handle_get_list_of_thumbnail_IDs(_handle, ids, count);

        return listed == count ? ids : ids[..Math.Clamp(listed, 0, count)];
    }

    public HeifImageHandle Thumbnail(uint id)
    {
        file.Check(heif_image_handle_get_thumbnail(_handle, id, out var thumbnail), $"Could not read thumbnail {id}");
        return new HeifImageHandle(thumbnail, file);
    }
    
    public byte[]? Exif()
    {
        var ids = new uint[1];
        if (heif_image_handle_get_list_of_metadata_block_IDs(_handle, "Exif", ids, 1) < 1)
            return null;

        var size = heif_image_handle_get_metadata_size(_handle, ids[0]);
        if (size < 4 || size > int.MaxValue)
            return null;

        var block = new byte[(int)size];
        if (heif_image_handle_get_metadata(_handle, ids[0], block).Failed)
            return null;

        var tiff = 4L + BinaryPrimitives.ReadUInt32BigEndian(block);
        return tiff < block.Length ? block[(int)tiff..] : null;
    }

    public byte[]? IccProfile()
    {
        if (heif_image_handle_get_color_profile_type(_handle) is not (ProfileIcc or ProfileRestrictedIcc))
            return null;

        var size = heif_image_handle_get_raw_color_profile_size(_handle);
        if (size == 0 || size > int.MaxValue)
            return null;

        var profile = new byte[(int)size];
        return heif_image_handle_get_raw_color_profile(_handle, profile).Failed ? null : profile;
    }

    public NclxProfile? Nclx()
    {
        if (heif_image_handle_get_nclx_color_profile(_handle, out var profile).Failed || profile == IntPtr.Zero)
            return null;

        try
        {
            return Marshal.PtrToStructure<NclxProfile>(profile);
        }
        finally
        {
            heif_nclx_color_profile_free(profile);
        }
    }
    
    public SKBitmap DecodeRgba(SKColorSpace? colorSpace, CancellationToken ct)
    {
        file.Check(heif_decode_image(_handle, out var decoded, ColorspaceRgb, ChromaInterleavedRgba, IntPtr.Zero), "Could not decode the image");
        using var image = new HeifImage(decoded);

        DecoderValidation.RequireSaneDimensions(image.Width, image.Height);
        return image.CopyRgba(image.Width, image.Height, colorSpace, ct);
    }

    public void Dispose()
    {
        if (_handle == IntPtr.Zero)
            return;

        heif_image_handle_release(_handle);
        _handle = IntPtr.Zero;
    }
}