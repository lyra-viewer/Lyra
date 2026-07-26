using System.Runtime.InteropServices;
using Lyra.Common;

namespace Lyra.SystemUtils;

public static partial class MacAboutPanel
{
    private const string Objc = "/usr/lib/libobjc.A.dylib";

    [LibraryImport(Objc, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr objc_getClass(string name);

    [LibraryImport(Objc, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr sel_registerName(string name);

    [LibraryImport(Objc)]
    private static partial IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

    [LibraryImport(Objc)]
    private static partial IntPtr objc_msgSend(IntPtr receiver, IntPtr selector, IntPtr arg1);

    public static void Show()
    {
        if (!OperatingSystem.IsMacOS())
            return;

        try
        {
            var nsApplication = objc_getClass("NSApplication");
            var sharedApp = objc_msgSend(nsApplication, sel_registerName("sharedApplication"));
            objc_msgSend(sharedApp, sel_registerName("orderFrontStandardAboutPanelWithOptions:"), IntPtr.Zero);
        }
        catch (Exception ex)
        {
            Logger.Warning($"[MacAboutPanel] Failed to show native About panel: {ex.Message}");
        }
    }
}