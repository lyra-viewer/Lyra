using System.Runtime.InteropServices;
using Lyra.Imaging.Content.Status;

namespace Lyra.Imaging.Interop;

internal static class NativeErrors
{
    public static string GetUtf8ZOrAnsiZ(IntPtr ptr)
        => ptr == IntPtr.Zero ? string.Empty : Marshal.PtrToStringAnsi(ptr) ?? string.Empty;

    /// <summary>A failed native decode, described by the library's own reason when it gave one.</summary>
    public static LoadFailureException DecodeFailed(IntPtr lastError, string path)
    {
        var reason = GetUtf8ZOrAnsiZ(lastError);
        return new LoadFailureException(LoadFailureKind.DecodeFailed, string.IsNullOrWhiteSpace(reason) ? $"Failed to decode: {path}" : reason);
    }
}