using System.Runtime.InteropServices;

namespace Lyra.Imaging.Interop;

internal static class NativeErrors
{
    public static string GetUtf8ZOrAnsiZ(IntPtr ptr)
        => ptr == IntPtr.Zero ? string.Empty : Marshal.PtrToStringAnsi(ptr) ?? string.Empty;
}