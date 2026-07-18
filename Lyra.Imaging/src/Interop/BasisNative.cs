using System.Runtime.InteropServices;

namespace Lyra.Imaging.Interop;

internal static class BasisNative
{
    [DllImport("libbasis_native", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool basis_decode_ktx2_rgba(
        byte[] data, int size, int level, int layer, int face,
        out IntPtr pixels, out int width, out int height);

    [DllImport("libbasis_native", CallingConvention = CallingConvention.Cdecl)]
    public static extern void basis_free_pixels(IntPtr ptr);

    [DllImport("libbasis_native", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr get_last_basis_error();
}