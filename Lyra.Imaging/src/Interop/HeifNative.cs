using System.Runtime.InteropServices;
using Lyra.Common;

namespace Lyra.Imaging.Interop;

/// <summary>
/// The parts of libheif's C API Lyra uses: still images, and image sequences (1.20 and later; an
/// older libheif lacks those entry points, which <see cref="SequencesAvailable"/> reports).
/// </summary>
internal static class HeifNative
{
    private const string Lib = "libheif";

    public const int ColorspaceRgb = 1;
    public const int ColorspaceMonochrome = 2;
    public const int ChromaMonochrome = 0;
    public const int ChromaInterleavedRgba = 11;
    public const int ChannelY = 0;
    public const int ChannelInterleaved = 10;
    public const int ErrorEndOfSequence = 13;
    public const uint RepetitionsInfinite = 0xFFFF_FFFF;

    public const uint ProfileRestrictedIcc = 0x72494343;  // 'rICC'
    public const uint ProfileIcc = 0x70726F66;            // 'prof'

    [StructLayout(LayoutKind.Sequential)]
    internal struct HeifError
    {
        public int Code;
        public int Subcode;
        public IntPtr Message;

        public readonly bool Failed => Code != 0;

        public readonly string Describe() =>
            (Message == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(Message))?.Replace("Unspecified: ", "") ?? $"libheif error {Code}.{Subcode}";

        public readonly void ThrowIfFailed(string what)
        {
            if (Failed)
                throw new HeifException($"{what}: {Describe()}");
        }
    }

    /// <summary>The leading fields of heif_color_profile_nclx, which is all Lyra reads; libheif allocates and frees it.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct NclxProfile
    {
        public byte Version;
        public int ColorPrimaries;
        public int TransferCharacteristics;
        public int MatrixCoefficients;
        public byte FullRangeFlag;
    }

    // --- Contexts

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr heif_context_alloc();

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void heif_context_free(IntPtr context);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern HeifError heif_context_read_from_reader(IntPtr context, IntPtr reader, IntPtr userdata, IntPtr options);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern HeifError heif_context_get_primary_image_ID(IntPtr context, out uint id);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr heif_get_version();

    public static string Version => Marshal.PtrToStringUTF8(heif_get_version()) ?? "unknown";

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern HeifError heif_context_get_primary_image_handle(IntPtr context, out IntPtr handle);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int heif_context_get_number_of_top_level_images(IntPtr context);

    // --- Image handles

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void heif_image_handle_release(IntPtr handle);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int heif_image_handle_get_width(IntPtr handle);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int heif_image_handle_get_height(IntPtr handle);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int heif_image_handle_has_alpha_channel(IntPtr handle);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int heif_image_handle_has_depth_image(IntPtr handle);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int heif_image_handle_get_number_of_thumbnails(IntPtr handle);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int heif_image_handle_get_list_of_thumbnail_IDs(IntPtr handle, [Out] uint[] ids, int count);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern HeifError heif_image_handle_get_thumbnail(IntPtr handle, uint id, out IntPtr thumbnail);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int heif_image_handle_get_list_of_metadata_block_IDs(IntPtr handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string typeFilter, [Out] uint[] ids, int count);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern nuint heif_image_handle_get_metadata_size(IntPtr handle, uint id);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern HeifError heif_image_handle_get_metadata(IntPtr handle, uint id, [Out] byte[] data);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint heif_image_handle_get_color_profile_type(IntPtr handle);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern nuint heif_image_handle_get_raw_color_profile_size(IntPtr handle);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern HeifError heif_image_handle_get_raw_color_profile(IntPtr handle, [Out] byte[] data);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern HeifError heif_image_handle_get_nclx_color_profile(IntPtr handle, out IntPtr profile);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void heif_nclx_color_profile_free(IntPtr profile);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern HeifError heif_decode_image(IntPtr handle, out IntPtr image, int colorspace, int chroma, IntPtr options);

    // --- Decoded images

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int heif_image_get_width(IntPtr image, int channel);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int heif_image_get_height(IntPtr image, int channel);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr heif_image_get_plane_readonly(IntPtr image, int channel, out int stride);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void heif_image_release(IntPtr image);

    // --- Sequences (1.20 and later)

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int heif_context_has_sequence(IntPtr context);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr heif_context_get_track(IntPtr context, uint id);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void heif_track_release(IntPtr track);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int heif_track_has_alpha_channel(IntPtr track);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern HeifError heif_track_get_image_resolution(IntPtr track, out ushort width, out ushort height);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint heif_track_get_number_of_repetitions(IntPtr track);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern HeifError heif_track_decode_next_image(IntPtr track, out IntPtr image, int colorspace, int chroma, IntPtr options);

    private static readonly Lazy<bool> SequenceSupport = new(ProbeSequenceSupport);

    /// <summary>Whether this libheif can read sequences; false, logged once, for one older than 1.20.</summary>
    public static bool SequencesAvailable => SequenceSupport.Value;

    private static bool ProbeSequenceSupport()
    {
        var context = heif_context_alloc();
        if (context == IntPtr.Zero)
            return false;

        try
        {
            heif_context_has_sequence(context);
            return true;
        }
        catch (EntryPointNotFoundException)
        {
            Logger.Warning(typeof(HeifNative), "This libheif predates image sequences (1.20); animated AVIF and HEIF show their still image only.");
            return false;
        }
        finally
        {
            heif_context_free(context);
        }
    }
}

/// <summary>A libheif call that failed, with libheif's own reason. Counts as a decode failure.</summary>
internal sealed class HeifException(string message) : InvalidOperationException(message);
