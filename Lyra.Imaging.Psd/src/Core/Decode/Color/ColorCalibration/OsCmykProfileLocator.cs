namespace Lyra.Imaging.Psd.Core.Decode.Color.ColorCalibration;

public static class OsCmykProfileLocator
{
    public static string? TryGetDefaultCmykIccPath()
    {
        if (OperatingSystem.IsMacOS())
        {
            var candidates = new[]
            {
                "/System/Library/ColorSync/Profiles/Generic CMYK Profile.icc",
                "/Library/ColorSync/Profiles/Generic CMYK Profile.icc",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library/ColorSync/Profiles/Generic CMYK Profile.icc")
            };

            return candidates.FirstOrDefault(File.Exists);
        }

        if (OperatingSystem.IsWindows())
        {
            // Best-effort: check Windows color directory for common CMYK working profiles.
            // (Maybe upgrade this later to call WCS APIs)
            var colorDir = Environment.ExpandEnvironmentVariables(@"%WINDIR%\System32\spool\drivers\color");
            if (!Directory.Exists(colorDir))
                return null;

            var candidates = new[]
            {
                "CoatedFOGRA39.icc",
                "ISOcoated_v2_300_eci.icc",
                "USWebCoatedSWOP.icc",
                "JapanColor2001Coated.icc",
            };

            foreach (var name in candidates)
            {
                var p = Path.Combine(colorDir, name);
                if (File.Exists(p)) return p;
            }

            return null;
        }

        // Linux / others
        var linuxCandidates = new[]
        {
            "/usr/share/color/icc",
            "/usr/local/share/color/icc"
        };

        foreach (var dir in linuxCandidates)
        {
            if (!Directory.Exists(dir))
                continue;

            var first = Directory.EnumerateFiles(dir, "*.icc").FirstOrDefault();
            if (first != null) return first;
        }

        return null;
    }
}