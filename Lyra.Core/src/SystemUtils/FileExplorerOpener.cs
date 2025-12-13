using System.Diagnostics;
using System.Runtime.InteropServices;
using Lyra.Common;

namespace Lyra.SystemUtils;

public static class FileExplorerOpener
{
    public static void RevealPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            var isDirectory = Directory.Exists(path);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                if (isDirectory)
                    Process.Start("open", $"\"{path}\"");
                else
                    Process.Start("open", $"-R \"{path}\"");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (isDirectory)
                    Process.Start("explorer.exe", $"\"{path}\"");
                else
                    Process.Start("explorer.exe", $"/select,\"{path}\"");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // xdg-open works for both files and directories
                var target = isDirectory ? path : Path.GetDirectoryName(path)!;
                Process.Start("xdg-open", $"\"{target}\"");
            }
            else
            {
                Logger.Warning("Unsupported platform");
            }
        }
        catch (Exception ex)
        {
            Logger.Warning($"RevealPath failed for '{path}': {ex.Message}");
        }
    }
}