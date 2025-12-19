using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using LibHeifSharp;
using Lyra.Common;
using SDL3;
using SkiaSharp;

namespace Lyra.Imaging;

internal static class NativeLibraryLoader
{
    private static readonly string LibPath;
    private static readonly string SystemName;
    private static readonly string MacOsFrameworkPath;

    private static readonly Dictionary<string, string> PathDictionary = new();
    private static readonly Dictionary<string, IntPtr> LoadedHandles = new();
    private static readonly List<string> SearchDirs = [];

    static NativeLibraryLoader()
    {
        var basePath = AppContext.BaseDirectory;
        MacOsFrameworkPath = Path.GetFullPath(Path.Combine(basePath, "..", "Frameworks")); // Adjusted for .app bundle
        LibPath = Path.Combine(basePath, "lib");

        SystemName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "Windows"
            : RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                ? "Linux"
                : "macOS";

        BuildSearchDirs(basePath);

        var platformLibraries = new Dictionary<string, string>
        {
            {
                "SDL3", RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "SDL3.dll" :
                RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "libSDL3.so" :
                "libSDL3.dylib"
            },
            {
                "LIBHEIF", RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "libheif.dll" :
                RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "libheif.so" :
                "libheif.dylib"
            },
            {
                "EXR", RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "libexr_native.dll" :
                RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "libexr_native.so" :
                "libexr_native.dylib"
            },
            {
                "HDR", RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "libhdr_native.dll" :
                RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "libhdr_native.so" :
                "libhdr_native.dylib"
            },
#if !DEBUG
            {
                "SKIA", RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "libSkiaSharp.dll" :
                RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "libSkiaSharp.so" :
                "libSkiaSharp.dylib"
            }
#endif
        };

        foreach (var (id, libName) in platformLibraries)
        {
            LocateLibrary(libName, id);
        }

        ResolveLibraries();
    }

    /// <summary>
    /// Call at app startup to eagerly load what we managed to locate.
    /// (DllImport resolvers will still work lazily.)
    /// </summary>
    public static void Initialize()
    {
        _ = Instance;

        foreach (var (id, path) in PathDictionary)
        {
            if (LoadedHandles.ContainsKey(id))
                continue;

            if (!File.Exists(path))
            {
                Logger.Error($"[NativeLibraryLoader] {id} path missing: {path}");
                continue;
            }

            var sw = Stopwatch.StartNew();
            try
            {
                var handle = NativeLibrary.Load(path);
                LoadedHandles[id] = handle;
                sw.Stop();
                Logger.Info($"[NativeLibraryLoader] Loaded {id} in {sw.Elapsed.TotalMilliseconds:F3} ms");
            }
            catch (Exception ex)
            {
                sw.Stop();
                Logger.Error($"[NativeLibraryLoader] Failed to load {id} from {path} after {sw.Elapsed.TotalMilliseconds:F3} ms: {ex}");
            }
        }
    }
    
    private static void BuildSearchDirs(string basePath)
    {
        SearchDirs.Clear();

        // App bundle Frameworks
        SearchDirs.Add(MacOsFrameworkPath);

        // Fallback inside publish output: <BaseDirectory>/lib/<SystemName>/
        SearchDirs.Add(Path.Combine(LibPath, SystemName));

        // Homebrew (macOS only) - for Homebrew-dependent distribution
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // Common brew lib dirs
            SearchDirs.Add("/opt/homebrew/lib"); // Apple Silicon
            SearchDirs.Add("/usr/local/lib");    // Intel
            
            SearchDirs.Add("/opt/homebrew/opt/sdl3/lib");
            SearchDirs.Add("/usr/local/opt/sdl3/lib");

            SearchDirs.Add("/opt/homebrew/opt/libheif/lib");
            SearchDirs.Add("/usr/local/opt/libheif/lib");

            SearchDirs.Add("/opt/homebrew/opt/openexr/lib");
            SearchDirs.Add("/usr/local/opt/openexr/lib");
        }

        // Keep only existing directories, de-dup while preserving order
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        SearchDirs.RemoveAll(d => string.IsNullOrWhiteSpace(d) || !Directory.Exists(d) || !seen.Add(d));
    }

    private static void LocateLibrary(string libraryName, string identifier)
    {
        foreach (var dir in SearchDirs)
        {
            var candidate = Path.Combine(dir, libraryName);
            if (File.Exists(candidate))
            {
                PathDictionary[identifier] = candidate;
                Logger.Info($"[NativeLibraryLoader] Located {identifier} at {candidate}");
                return;
            }
        }

        Logger.Error($"[NativeLibraryLoader] Failed to locate {libraryName}.");
    }

    private static void ResolveLibraries()
    {
        NativeLibrary.SetDllImportResolver(typeof(SDL).Assembly, ResolveSdl);
        NativeLibrary.SetDllImportResolver(typeof(LibHeifInfo).Assembly, ResolveHeif);
        NativeLibrary.SetDllImportResolver(typeof(NativeLibraryLoader).Assembly, ResolveInterop);

#if !DEBUG
        NativeLibrary.SetDllImportResolver(typeof(SKImage).Assembly, ResolveSkia);
#endif
    }

    private static IntPtr ResolveSdl(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        return libraryName switch
        {
            "SDL3" or "SDL3.dll" or "libSDL3.so" or "libSDL3.dylib" => NativeLibrary.Load(PathDictionary["SDL3"]),
            _ => IntPtr.Zero
        };
    }

    private static IntPtr ResolveSkia(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        return libraryName switch
        {
            "libSkiaSharp" or "libSkiaSharp.dll" or "libSkiaSharp.so" or "libSkiaSharp.dylib" => NativeLibrary.Load(PathDictionary["SKIA"]),
            _ => IntPtr.Zero
        };
    }

    private static IntPtr ResolveHeif(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        return libraryName switch
        {
            "libheif" or "libheif.dll" or "libheif.so" or "libheif.dylib" => NativeLibrary.Load(PathDictionary["LIBHEIF"]),
            _ => IntPtr.Zero
        };
    }

    private static IntPtr ResolveInterop(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        return libraryName switch
        {
            "libexr" or "libexr.dll" or "libexr.so" or "libexr.dylib" => NativeLibrary.Load(PathDictionary["EXR"]),
            "libhdr" or "libhdr.dll" or "libhdr.so" or "libhdr.dylib" => NativeLibrary.Load(PathDictionary["HDR"]),
            _ => IntPtr.Zero
        };
    }

    public static readonly object Instance = new();
}