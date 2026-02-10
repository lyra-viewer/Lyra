using System.Runtime.InteropServices;

namespace Lyra.Imaging.Interop;

internal static class J2KNative
{
    [DllImport("libj2k_native", CallingConvention = CallingConvention.Cdecl)]
    public static extern bool decode_j2k_rgba8_from_memory(
        IntPtr data,
        nuint size,
        int reduce,
        out IntPtr pixels,
        out int width,
        out int height,
        out int strideBytes);

    [DllImport("libj2k_native", CallingConvention = CallingConvention.Cdecl)]
    public static extern void free_j2k_pixels(IntPtr ptr);

    [DllImport("libj2k_native", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr get_last_j2k_error();
}