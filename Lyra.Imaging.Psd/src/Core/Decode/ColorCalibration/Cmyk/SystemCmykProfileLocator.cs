namespace Lyra.Imaging.Psd.Core.Decode.ColorCalibration.Cmyk;

public static class SystemCmykProfileLocator
{
    private static readonly byte[] CmykSignature = "CMYK"u8.ToArray();

    public static string? TryGetDefaultCmykIccPath()
    {
        if (OperatingSystem.IsMacOS())
        {
            var candidates = new[]
            {
                "/System/Library/ColorSync/Profiles/Generic CMYK Profile.icc",
                "/Library/ColorSync/Profiles/Generic CMYK Profile.icc",
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Library/ColorSync/Profiles/Generic CMYK Profile.icc"
                )
            };

            return candidates.FirstOrDefault(File.Exists);
        }

        if (OperatingSystem.IsWindows())
        {
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

        // Linux / others — search known color directories for CMYK .icc files.
        var linuxDirs = new[]
        {
            "/usr/share/color/icc",
            "/usr/share/color/icc/ghostscript",
            "/usr/local/share/color/icc",
        };

        foreach (var dir in linuxDirs)
        {
            if (!Directory.Exists(dir))
                continue;

            try
            {
                foreach (var file in Directory.EnumerateFiles(dir, "*.icc", SearchOption.AllDirectories))
                {
                    if (IsCmykProfile(file))
                        return file;
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Permission denied on some subdirectory — skip.
            }
            catch (IOException)
            {
                // Broken symlink, etc.
            }
        }

        return null;
    }

    private static bool IsCmykProfile(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (fs.Length < 20)
                return false;

            // ICC header is 128 bytes; color space at offset 16.
            Span<byte> header = stackalloc byte[20];
            if (fs.Read(header) < 20)
                return false;

            return header[16] == CmykSignature[0]
                   && header[17] == CmykSignature[1]
                   && header[18] == CmykSignature[2]
                   && header[19] == CmykSignature[3];
        }
        catch
        {
            return false;
        }
    }
}