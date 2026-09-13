using System.Runtime.InteropServices;
using Lyra.Common;
using SkiaSharp;

namespace Lyra.Imaging.Decoding.Support;

internal static class IccColorSpace
{
    public static SKColorSpace? FromNative(IntPtr profile, int byteCount, string decoder)
    {
        if (profile == IntPtr.Zero || byteCount <= 0)
            return null;

        try
        {
            var icc = new byte[byteCount];
            Marshal.Copy(profile, icc, 0, byteCount);

            return SKColorSpace.CreateIcc(icc);
        }
        catch (Exception ex)
        {
            Logger.Debug($"[{decoder}] ICC profile parse failed: {ex.Message}");
            return null;
        }
    }
}