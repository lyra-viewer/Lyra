using System.Runtime.InteropServices;

namespace Lyra.SystemUtils.MacInterop;

/// <summary>
/// The macOS frameworks Lyra calls directly rather than through the Objective-C runtime: Metal
/// for the device, CoreGraphics for colour spaces.
/// </summary>
internal static class Native
{
    private const string Metal = "/System/Library/Frameworks/Metal.framework/Metal";
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const int RtldLazy = 1;

    [DllImport(Metal)] private static extern IntPtr MTLCreateSystemDefaultDevice();
    [DllImport(Metal)] private static extern IntPtr MTLCopyAllDevices();

    [DllImport(CoreGraphics)] private static extern IntPtr CGColorSpaceCreateWithName(IntPtr name);
    [DllImport(CoreGraphics)] public static extern void CGColorSpaceRelease(IntPtr space);

    [DllImport("/usr/lib/libSystem.B.dylib")]
    private static extern IntPtr dlopen(string path, int mode);

    [DllImport("/usr/lib/libSystem.B.dylib")]
    private static extern IntPtr dlsym(IntPtr handle, string symbol);

    /// <summary>
    /// The Metal device, preferring the system default but not trusting it to exist.
    /// <paramref name="source"/> reports which route produced it, which is itself a diagnostic.
    /// </summary>
    public static IntPtr CreateMetalDevice(out string source)
    {
        var device = MTLCreateSystemDefaultDevice();
        if (device != IntPtr.Zero)
        {
            source = "MTLCreateSystemDefaultDevice";
            return device;
        }

        var devices = MTLCopyAllDevices();
        if (devices != IntPtr.Zero && ObjC.SendUInt64(devices, "count") > 0)
        {
            source = "MTLCopyAllDevices[0] (system default was null)";
            return ObjC.Send(devices, "objectAtIndex:", 0UL);
        }

        source = "none";
        return IntPtr.Zero;
    }

    /// <summary>
    /// Builds the colour space named by a <c>kCGColorSpace*</c> global, or zero when this OS
    /// version does not have that constant.
    /// </summary>
    public static IntPtr CreateColorSpace(string constantName)
    {
        var handle = dlopen(CoreGraphics, RtldLazy);
        if (handle == IntPtr.Zero)
            return IntPtr.Zero;

        var symbol = dlsym(handle, constantName);
        if (symbol == IntPtr.Zero)
            return IntPtr.Zero;

        var cfName = Marshal.ReadIntPtr(symbol);
        return cfName == IntPtr.Zero ? IntPtr.Zero : CGColorSpaceCreateWithName(cfName);
    }
}