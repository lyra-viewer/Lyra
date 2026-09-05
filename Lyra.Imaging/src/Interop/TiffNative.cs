using System.Runtime.InteropServices;

namespace Lyra.Imaging.Interop;

internal static class TiffNative
{
    [DllImport("libtiff_native", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)] // native returns a 1-byte C++ bool
    public static extern bool load_tiff_rgba(string path,
        out IntPtr pixels, out int width, out int height, out IntPtr icc, out int iccSize);

    [DllImport("libtiff_native", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool load_tiff_rgba_mem(IntPtr data, ulong size, out IntPtr pixels, out int width, out int height, out IntPtr icc, out int iccSize);

    [DllImport("libtiff_native", CallingConvention = CallingConvention.Cdecl)]
    public static extern void free_tiff_pixels(IntPtr ptr);

    private static bool _memoryEntryPointMissing;

    public static bool MemoryLoadAvailable => !Volatile.Read(ref _memoryEntryPointMissing);

    public static bool LoadFromMemory(IntPtr data, ulong size, out IntPtr pixels, out int width, out int height, out IntPtr icc, out int iccSize)
    {
        try
        {
            return load_tiff_rgba_mem(data, size, out pixels, out width, out height, out icc, out iccSize);
        }
        catch (EntryPointNotFoundException)
        {
            Volatile.Write(ref _memoryEntryPointMissing, true);
            pixels = IntPtr.Zero;
            width = height = 0;
            icc = IntPtr.Zero;
            iccSize = 0;
            return false;
        }
    }

    [DllImport("libtiff_native", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr get_last_tiff_error();
}