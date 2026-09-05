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
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool load_exr_rgba_mem(IntPtr data, ulong size, out IntPtr pixels, out int width, out int height, out ExrInfo info);

    [DllImport("libexr_native", CallingConvention = CallingConvention.Cdecl)]
    public static extern void free_exr_pixels(IntPtr ptr);

    private static bool _memoryEntryPointMissing;

    public static bool MemoryLoadAvailable => !Volatile.Read(ref _memoryEntryPointMissing);

    public static bool LoadFromMemory(IntPtr data, ulong size, out IntPtr pixels, out int width, out int height, out ExrInfo info)
    {
        try
        {
            return load_exr_rgba_mem(data, size, out pixels, out width, out height, out info);
        }
        catch (EntryPointNotFoundException)
        {
            Volatile.Write(ref _memoryEntryPointMissing, true);
            pixels = IntPtr.Zero;
            width = height = 0;
            info = default;
            return false;
        }
    }

    [DllImport("libexr_native", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr get_last_exr_error();
}