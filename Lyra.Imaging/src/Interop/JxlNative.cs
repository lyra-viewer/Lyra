using System.Runtime.InteropServices;

namespace Lyra.Imaging.Interop;

internal static class JxlNative
{
    [DllImport("libjxl_native", CallingConvention = CallingConvention.Cdecl)]
    public static extern bool decode_jxl_from_memory(
        IntPtr data,
        nuint size,
        out int width,
        out int height,
        out int isHdr,
        out int bitsPerSample,
        out int hasAlpha,
        out int hasAnimation,
        out IntPtr pixels);

    [DllImport("libjxl_native", CallingConvention = CallingConvention.Cdecl)]
    public static extern void free_jxl_pixels(IntPtr ptr);

    [DllImport("libjxl_native", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr get_last_jxl_error();
}