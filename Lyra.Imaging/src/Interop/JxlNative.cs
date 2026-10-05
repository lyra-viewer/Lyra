using System.Runtime.InteropServices;
using Lyra.Common;

namespace Lyra.Imaging.Interop;

internal static class JxlNative
{
    [DllImport("libjxl_native", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)] // native returns a 1-byte C++ bool
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

    [StructLayout(LayoutKind.Sequential)]
    internal struct AnimationInfo
    {
        public int Width;
        public int Height;
        public int IsHdr;
        public int BitsPerSample;
        public int HasAlpha;
        public int FrameCount;
        public int LoopCount;
        public int Incomplete;
        public int FileColorSpace;
    }

    public const int AnimationTruncated = 1;
    public const int AnimationBroken = 2;

    internal sealed class AnimationHandle() : SafeHandle(IntPtr.Zero, ownsHandle: true)
    {
        public override bool IsInvalid => handle == IntPtr.Zero;

        protected override bool ReleaseHandle()
        {
            jxl_animation_close(handle);
            return true;
        }
    }

    [DllImport("libjxl_native", CallingConvention = CallingConvention.Cdecl)]
    private static extern AnimationHandle jxl_animation_open(IntPtr data, nuint size, out AnimationInfo info);

    [DllImport("libjxl_native", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool jxl_animation_frame_durations(AnimationHandle animation, [Out] int[] durationsMs, int count);

    [DllImport("libjxl_native", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool jxl_animation_decode_frame(AnimationHandle animation, int index, out IntPtr pixels, out int partial);

    [DllImport("libjxl_native", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool jxl_animation_icc(AnimationHandle animation, out IntPtr icc, out nuint size);

    [DllImport("libjxl_native", CallingConvention = CallingConvention.Cdecl)]
    private static extern void jxl_animation_close(IntPtr animation);

    public static byte[]? AnimationIcc(AnimationHandle animation)
    {
        if (!jxl_animation_icc(animation, out var icc, out var size) || icc == IntPtr.Zero || size == 0 || size > int.MaxValue)
            return null;

        var bytes = new byte[(int)size];
        Marshal.Copy(icc, bytes, 0, bytes.Length);
        return bytes;
    }

    private static bool _animationEntryPointsMissing;
    
    public static AnimationHandle? OpenAnimation(IntPtr data, nuint size, out AnimationInfo info)
    {
        info = default;

        if (Volatile.Read(ref _animationEntryPointsMissing))
            return null;

        try
        {
            var handle = jxl_animation_open(data, size, out info);
            if (!handle.IsInvalid)
                return handle;

            handle.Dispose();
            return null;
        }
        catch (EntryPointNotFoundException)
        {
            Volatile.Write(ref _animationEntryPointsMissing, true);
            Logger.Warning(typeof(JxlNative), "The native JPEG XL library has no animation entry points. It is older than this " +
                                              "build expects, so animated JPEG XL shows its first frame only until it is rebuilt.");
            return null;
        }
    }
}