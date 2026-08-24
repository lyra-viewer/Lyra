using System.Runtime.InteropServices;

namespace Lyra.SystemUtils.MacInterop;

internal static class ObjC
{
    private const string Libobjc = "/usr/lib/libobjc.A.dylib";

    [DllImport(Libobjc)] private static extern IntPtr sel_registerName(string name);
    [DllImport(Libobjc)] private static extern IntPtr objc_getClass(string name);

    [DllImport(Libobjc, EntryPoint = "objc_msgSend")]
    private static extern IntPtr msg_IntPtr(IntPtr receiver, IntPtr selector);

    [DllImport(Libobjc, EntryPoint = "objc_msgSend")]
    private static extern IntPtr msg_IntPtr_IntPtr(IntPtr receiver, IntPtr selector, IntPtr arg);

    [DllImport(Libobjc, EntryPoint = "objc_msgSend")]
    private static extern IntPtr msg_IntPtr_UInt64(IntPtr receiver, IntPtr selector, ulong arg);

    [DllImport(Libobjc, EntryPoint = "objc_msgSend")]
    private static extern IntPtr msg_IntPtr_TextureDescriptor(IntPtr receiver, IntPtr selector, ulong pixelFormat, ulong width, ulong height, [MarshalAs(UnmanagedType.I1)] bool mipmapped);

    [DllImport(Libobjc, EntryPoint = "objc_msgSend")]
    private static extern void msg_Void(IntPtr receiver, IntPtr selector);

    [DllImport(Libobjc, EntryPoint = "objc_msgSend")]
    private static extern void msg_Void_IntPtr(IntPtr receiver, IntPtr selector, IntPtr arg);

    [DllImport(Libobjc, EntryPoint = "objc_msgSend")]
    private static extern void msg_Void_UInt64(IntPtr receiver, IntPtr selector, ulong arg);

    [DllImport(Libobjc, EntryPoint = "objc_msgSend")]
    private static extern void msg_Void_Bool(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.I1)] bool arg);

    [DllImport(Libobjc, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool msg_Bool(IntPtr receiver, IntPtr selector);

    [DllImport(Libobjc, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool msg_Bool_IntPtr(IntPtr receiver, IntPtr selector, IntPtr arg);

    [DllImport(Libobjc, EntryPoint = "objc_msgSend")]
    private static extern ulong msg_UInt64(IntPtr receiver, IntPtr selector);

    [DllImport(Libobjc, EntryPoint = "objc_msgSend")]
    private static extern double msg_Double_arm(IntPtr receiver, IntPtr selector);

    [DllImport(Libobjc, EntryPoint = "objc_msgSend_fpret")]
    private static extern double msg_Double_x64(IntPtr receiver, IntPtr selector);

    private static readonly bool IsArm = RuntimeInformation.ProcessArchitecture is Architecture.Arm64;

    // -------------------------------------------------------------------------
    //  Classes and selectors
    // -------------------------------------------------------------------------

    public static IntPtr Sel(string name) => sel_registerName(name);

    public static IntPtr Class(string name) => objc_getClass(name);

    // -------------------------------------------------------------------------
    //  Sending messages
    // -------------------------------------------------------------------------

    public static IntPtr Send(IntPtr receiver, IntPtr selector) => msg_IntPtr(receiver, selector);
    public static IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr arg) => msg_IntPtr_IntPtr(receiver, selector, arg);
    public static IntPtr Send(IntPtr receiver, IntPtr selector, ulong arg) => msg_IntPtr_UInt64(receiver, selector, arg);

    public static IntPtr Send(IntPtr receiver, string selector) => msg_IntPtr(receiver, Sel(selector));
    public static IntPtr Send(IntPtr receiver, string selector, IntPtr arg) => msg_IntPtr_IntPtr(receiver, Sel(selector), arg);
    public static IntPtr Send(IntPtr receiver, string selector, ulong arg) => msg_IntPtr_UInt64(receiver, Sel(selector), arg);

    public static void SendVoid(IntPtr receiver, IntPtr selector) => msg_Void(receiver, selector);
    public static void SendVoid(IntPtr receiver, IntPtr selector, IntPtr arg) => msg_Void_IntPtr(receiver, selector, arg);
    public static void SendVoid(IntPtr receiver, IntPtr selector, ulong arg) => msg_Void_UInt64(receiver, selector, arg);
    public static void SendVoid(IntPtr receiver, IntPtr selector, bool arg) => msg_Void_Bool(receiver, selector, arg);

    public static void SendVoid(IntPtr receiver, string selector) => msg_Void(receiver, Sel(selector));
    public static void SendVoid(IntPtr receiver, string selector, IntPtr arg) => msg_Void_IntPtr(receiver, Sel(selector), arg);
    public static void SendVoid(IntPtr receiver, string selector, ulong arg) => msg_Void_UInt64(receiver, Sel(selector), arg);
    public static void SendVoid(IntPtr receiver, string selector, bool arg) => msg_Void_Bool(receiver, Sel(selector), arg);

    public static bool SendBool(IntPtr receiver, IntPtr selector) => msg_Bool(receiver, selector);
    public static bool SendBool(IntPtr receiver, string selector) => msg_Bool(receiver, Sel(selector));

    public static ulong SendUInt64(IntPtr receiver, IntPtr selector) => msg_UInt64(receiver, selector);
    public static ulong SendUInt64(IntPtr receiver, string selector) => msg_UInt64(receiver, Sel(selector));

    /// A CGFloat return - see the type remarks for why this is not one <c>DllImport</c>.
    public static double SendDouble(IntPtr receiver, IntPtr selector)
        => IsArm ? msg_Double_arm(receiver, selector) : msg_Double_x64(receiver, selector);

    public static double SendDouble(IntPtr receiver, string selector) 
        => SendDouble(receiver, Sel(selector));

    /// <c>+[MTLTextureDescriptor texture2DDescriptorWithPixelFormat:width:height:mipmapped:]</c>,
    /// the one message Lyra sends that needs four arguments.
    public static IntPtr SendTextureDescriptor(IntPtr receiver, string selector, ulong pixelFormat, ulong width, ulong height, bool mipmapped)
        => msg_IntPtr_TextureDescriptor(receiver, Sel(selector), pixelFormat, width, height, mipmapped);

    /// <summary>
    /// Whether the receiver implements the selector. AppKit gained the EDR selectors across several
    /// macOS releases, so asking is the difference between a missing value and a crash.
    /// </summary>
    public static bool Responds(IntPtr receiver, IntPtr selector)
        => receiver != IntPtr.Zero && msg_Bool_IntPtr(receiver, Sel("respondsToSelector:"), selector);

    public static bool Responds(IntPtr receiver, string selector) => Responds(receiver, Sel(selector));
}