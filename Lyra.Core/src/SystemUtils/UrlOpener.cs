using System.Diagnostics;
using System.Runtime.InteropServices;
using Lyra.Common;

namespace Lyra.SystemUtils;

public static class UrlOpener
{
    public static void Open(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", $"\"{url}\"");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Process.Start("xdg-open", $"\"{url}\"");
            }
            else
            {
                Logger.Warning($"[UrlOpener] Unsupported platform; cannot open {url}");
            }
        }
        catch (Exception ex)
        {
            Logger.Warning($"[UrlOpener] Failed to open '{url}': {ex.Message}");
        }
    }
}