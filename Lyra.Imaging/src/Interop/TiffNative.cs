using System.Runtime.InteropServices;

namespace Lyra.Imaging.Interop;

internal static class TiffNative
{
    [DllImport("libtiff_native", CallingConvention = CallingConvention.Cdecl)]
    public static extern bool load_tiff_rgba(string path, 
        out IntPtr pixels, out int width, out int height, out IntPtr icc, out int iccSize);

    [DllImport("libtiff_native", CallingConvention = CallingConvention.Cdecl)]
    public static extern void free_tiff_pixels(IntPtr ptr);

    [DllImport("libtiff_native", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr get_last_tiff_error();
}
