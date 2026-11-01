using System.Runtime.InteropServices;
using Lyra.Imaging.Decoding.Decoders;

namespace Lyra.Imaging.Tests.Support;

/// <summary>The system libheif, loaded once for every test that needs it; tests skip where it is not installed.</summary>
internal static class Libheif
{
    private static readonly string[] Candidates = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? ["libheif.dll", "heif.dll"]
        : RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            ? ["libheif.so.1", "libheif.so"]
            : ["/opt/homebrew/opt/libheif/lib/libheif.dylib", "/usr/local/opt/libheif/lib/libheif.dylib", "libheif.dylib"];

    private static readonly Lazy<bool> Loaded = new(() => NativeWrapper.TryLoadSystem("libheif", Candidates, typeof(HeifDecoder).Assembly));

    public static bool Ready => Loaded.Value;
}
