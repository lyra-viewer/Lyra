using Lyra.Common;
using Lyra.SystemUtils.MacInterop;

namespace Lyra.SystemUtils;

public static class MacAboutPanel
{
    public static void Show()
    {
        if (!OperatingSystem.IsMacOS())
            return;

        try
        {
            var sharedApp = ObjC.Send(ObjC.Class("NSApplication"), "sharedApplication");
            ObjC.Send(sharedApp, "orderFrontStandardAboutPanelWithOptions:", IntPtr.Zero);
        }
        catch (Exception ex)
        {
            Logger.Warning($"[MacAboutPanel] Failed to show native About panel: {ex.Message}");
        }
    }
}