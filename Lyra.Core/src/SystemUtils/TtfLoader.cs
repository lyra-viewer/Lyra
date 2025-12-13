using System.Runtime.InteropServices;
using Lyra.Common;

namespace Lyra.SystemUtils;

public static class TtfLoader
{
    private static readonly string[] WindowsMonospaceFonts =
    [
        "lucon.ttf",    // Lucida Console
        "consola.ttf"   // Consolas
    ];

    private static readonly string[] MacMonospaceFonts =
    [
        "/System/Library/Fonts/Menlo.ttc",
        "/System/Library/Fonts/Monaco.ttf"
    ];

    private static readonly string[] LinuxMonospaceFonts =
    [
        "/usr/share/fonts/truetype/dejavu/DejaVuSansMono.ttf",
        "/usr/share/fonts/truetype/liberation/LiberationMono-Regular.ttf",
        "/usr/share/fonts/truetype/noto/NotoMono-Regular.ttf"
    ];

    public static string GetMonospaceFontPath()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return FindExistingFont(WindowsMonospaceFonts, Environment.GetFolderPath(Environment.SpecialFolder.Fonts));

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return FindExistingFont(MacMonospaceFonts);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return FindExistingFont(LinuxMonospaceFonts);

        throw new Exception("[TtfLoader] No valid monospace font found.");
    }

    private static string FindExistingFont(string[] fontNames, string? basePath = null)
    {
        foreach (var font in fontNames)
        {
            var fontPath = basePath != null ? Path.Combine(basePath, font) : font;
            if (File.Exists(fontPath))
            {
                Logger.Info($"[TtfLoader] Using font: {fontPath}");
                return fontPath;
            }
        }

        throw new Exception("[TtfLoader] No valid system font found.");
    }
}