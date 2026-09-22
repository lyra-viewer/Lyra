using System.Reflection;
using System.Runtime.InteropServices;

namespace Lyra.Imaging.Tests.Support;

/// <summary>
/// Finds and loads a built native wrapper, for tests that have to go through the real library
/// rather than a managed stand-in. Wrappers are built into the private release repo's
/// dist-&lt;platform&gt; directory and are not copied next to the tests, so they are located by
/// walking up from the test binary and loaded by absolute path.
/// </summary>
internal static class NativeWrapper
{
    private static readonly Dictionary<Assembly, Dictionary<string, IntPtr>> Loaded = new();

    public static bool TryLoad(string baseName, Assembly importedBy, Action? probe = null)
    {
        var path = Locate(baseName);
        if (path is null)
            return false;

        try
        {
            Register(importedBy, baseName, NativeLibrary.Load(path));
        }
        catch
        {
            return false;
        }

        try
        {
            probe?.Invoke();
            return true;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return false;
        }
    }

    private static void Register(Assembly assembly, string baseName, IntPtr handle)
    {
        lock (Loaded)
        {
            if (!Loaded.TryGetValue(assembly, out var handles))
            {
                handles = new Dictionary<string, IntPtr>(StringComparer.OrdinalIgnoreCase);
                Loaded[assembly] = handles;

                NativeLibrary.SetDllImportResolver(assembly, (name, _, _) =>
                {
                    lock (Loaded)
                        return handles.FirstOrDefault(pair => name.Contains(pair.Key, StringComparison.OrdinalIgnoreCase)).Value;
                });
            }

            handles[baseName] = handle;
        }
    }

    private static string? Locate(string baseName)
    {
        var (leaf, distDir) =
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? ($"{baseName}.dll", "dist-windows")
                : RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                    ? ($"{baseName}.so", "dist-linux")
                    : ($"{baseName}.dylib", "dist-macos");

        var relative = Path.Combine("release", "native", distDir);

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, relative, leaf);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }
}