using System.Runtime.InteropServices;

namespace Lyra.Imaging.Interop;

internal static class NativeErrors
{
    public static string GetUtf8ZOrAnsiZ(IntPtr ptr)
        => ptr == IntPtr.Zero ? string.Empty : Marshal.PtrToStringAnsi(ptr) ?? string.Empty;

    /// <summary>A failed native decode, described by the library's own reason when it gave one.</summary>
    public static InvalidOperationException DecodeFailed(IntPtr lastError, string path)
    {
        var reason = GetUtf8ZOrAnsiZ(lastError);
        return new InvalidOperationException(string.IsNullOrWhiteSpace(reason) ? $"Failed to decode: {path}" : reason);
    }
}