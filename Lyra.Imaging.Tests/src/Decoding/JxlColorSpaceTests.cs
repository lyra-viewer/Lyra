using System.Runtime.InteropServices;
using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Decoders;
using SkiaSharp;
using Xunit;

namespace Lyra.Imaging.Tests.Decoding;

/// <summary>
/// Regression test for the JXL wide-gamut fix: a Display-P3 JXL must decode into distinct
/// wide-gamut pixels (not collapse to solid red) and be tagged Display-P3. Unlike the DDS/KTX
/// synthesis tests, JXL is a compressed bitstream decoded by native libjxl, so this uses a
/// tiny committed real asset and the native decoder. It self-skips when the native wrapper is
/// not present (e.g. CI without the native build), keeping the suite portable.
/// </summary>
public class JxlColorSpaceTests
{
    // 8x1 Display-P3 JXL (cjxl -d 0): left half pure P3 red ("logo"), right half sRGB red
    // re-encoded into P3 ("background"). Under the pre-fix sRGB-clamp wrapper both halves
    // decoded to identical solid red; the fix keeps them distinct.
    private const string DisplayP3JxlBase64 =
        "/woAAA6AJdwvDEiYBkBzSga4OPM6Vvp+izjMXhVa+JLcLQrZKwgqIZyZTe7qYWgFzGyh/CKQV5wLZrH4Aou" +
        "PY2nFV1hc6jqhwbCEqbOwrKF5gFBmgIehrbV0gLeptGiAs5KHQoCUsqGus6alMtBBvZBeX//9998dqNQHjc" +
        "XCVXMQlA0Tl1Bkm1B7rSyATtM4AJumJlTeE9yawUyhT54b77r6Xy8BDHv4+nrKWgaY6DrGBUQGJsYCCAAQA" +
        "GQASxiLFcIJMm/CexcQAWD+l9bf5wU1hlsRAg==";

    private static readonly SKColorSpace DisplayP3 =
        SKColorSpace.CreateRgb(SKColorSpaceTransferFn.Srgb, SKColorSpaceXyz.DisplayP3);

    // Load + wire the native wrapper once; false when it (or its deps) can't be found/loaded.
    private static readonly Lazy<bool> NativeJxlReady = new(TryPrepareNativeJxl);

    [Fact]
    public void DecodesDisplayP3Jxl_PreservesWideGamut_AndTagsDisplayP3()
    {
        if (!NativeJxlReady.Value)
            Assert.Skip("libjxl_native not available (native wrappers not built for this platform).");

        var tempPath = Path.Combine(Path.GetTempPath(), $"lyra-p3-{Guid.NewGuid():N}.jxl");
        File.WriteAllBytes(tempPath, Convert.FromBase64String(DisplayP3JxlBase64));

        try
        {
            using var composite = new Composite(new FileInfo(tempPath));
            new JxlDecoder().DecodeAsync(composite, CancellationToken.None).GetAwaiter().GetResult();

            var raster = Assert.IsType<RasterContent>(composite.Content);
            using var image = raster.Image;

            // 1. Tagged Display-P3 (guards JxlDecoder.BuildSdr / the wrapper's P3 output).
            Assert.NotNull(image.ColorSpace);
            Assert.False(image.ColorSpace.IsSrgb, "decoded JXL should be tagged wide-gamut, not sRGB");
            Assert.True(SKColorSpace.Equal(image.ColorSpace, DisplayP3), "decoded JXL should be tagged Display-P3");

            // 2. Wide gamut preserved: the logo and background reds must NOT collapse.
            using var bitmap = SKBitmap.FromImage(image);
            var logo = bitmap.GetPixel(0, 0);
            var background = bitmap.GetPixel(bitmap.Width - 1, 0);
            Assert.NotEqual(logo, background);
        }
        finally
        {
            try
            {
                File.Delete(tempPath);
            }
            catch
            {
                /* best effort cleanup */
            }
        }
    }

    private static bool TryPrepareNativeJxl()
    {
        var dylibPath = LocateJxlNative();
        if (dylibPath is null)
            return false;

        try
        {
            var handle = NativeLibrary.Load(dylibPath); // resolves libjxl deps via the wrapper's rpaths
            try
            {
                NativeLibrary.SetDllImportResolver(typeof(JxlDecoder).Assembly, (name, _, _) =>
                    name is "libjxl_native" or "libjxl_native.dll" or "libjxl_native.so"
                        or "libjxl_native.dylib" or "libjxl"
                        ? handle
                        : IntPtr.Zero);
            }
            catch (InvalidOperationException)
            {
                // A resolver was already set for this assembly; the eager Load above still
                // brought the module into the process, so decoding can proceed.
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    // Walks up from the test output directory to the repo root and returns the built native
    // JXL wrapper for this platform, or null if it hasn't been built.
    private static string? LocateJxlNative()
    {
        var (leaf, distDir) =
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? ("libjxl_native.dll", "dist-windows")
                : RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                    ? ("libjxl_native.so", "dist-linux")
                    : ("libjxl_native.dylib", "dist-macos");

        string[] relDirs =
        [
            Path.Combine("release", "native", distDir),
            Path.Combine("native", distDir),
        ];

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            foreach (var relDir in relDirs)
            {
                var candidate = Path.Combine(dir.FullName, relDir, leaf);
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }
}