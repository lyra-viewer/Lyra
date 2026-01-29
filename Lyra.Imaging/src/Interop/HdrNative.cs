using System.Runtime.InteropServices;

namespace Lyra.Imaging.Interop;

internal static class HdrNative
{
    [DllImport("libhdr_native", CallingConvention = CallingConvention.Cdecl)]
    public static extern bool load_hdr_rgba(string path, out IntPtr pixels, out int width, out int height);

    [DllImport("libhdr_native", CallingConvention = CallingConvention.Cdecl)]
    public static extern void free_hdr_pixels(IntPtr ptr);

    [DllImport("libhdr_native", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr get_last_hdr_error();
}