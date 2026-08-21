using System.Runtime.InteropServices;

namespace Lyra.Imaging.Interop;

internal static class ExrNative
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct ExrInfo
    {
        public int BitsPerChannel;
        public int IsFloat;
        public int HasAlpha;
        public int IsGray;
        public int CustomPrimaries;
    }

    [DllImport("libexr_native", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)] // native returns a 1-byte C++ bool
    public static extern bool load_exr_rgba(string path, out IntPtr pixels, out int width, out int height, out ExrInfo info);

    [DllImport("libexr_native", CallingConvention = CallingConvention.Cdecl)]
    public static extern void free_exr_pixels(IntPtr ptr);

    [DllImport("libexr_native", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr get_last_exr_error();
}