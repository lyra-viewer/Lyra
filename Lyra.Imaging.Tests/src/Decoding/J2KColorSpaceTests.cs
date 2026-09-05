using System.Runtime.InteropServices;
using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Decoders;
using SkiaSharp;
using Xunit;

namespace Lyra.Imaging.Tests.Decoding;

/// <summary>
/// Regression tests for the JPEG 2000 color space fix. OpenJPEG decodes the codestream but never
/// converts color spaces - opj_decode hands back sYCC as luma and chroma, and CMYK as ink - so a
/// wrapper that reads the components as RGB shows false color.
/// </summary>
public class J2KColorSpaceTests
{
    // 2x2 sYCC JP2 (opj_compress -F 2,2,3,8,u -n 1 -mct 0, which tags EnumCS 18). Components are
    // stored, not derived: the left column is Y=128 Cb=128 Cr=128 (neutral), the right column is
    // Y=76 Cb=85 Cr=255, which is red once converted and a mid-blue if it is not.
    private const string SyccJp2Base64 =
        "AAAADGpQICANCocKAAAAFGZ0eXBqcDIgAAAAAGpwMiAAAAAtanAyaAAAABZpaGRyAAAAAgAAAAIAAwcHAAAAAAAP" +
        "Y29scgEAAAAAABIAAACYanAyY/9P/1EALwAAAAAAAgAAAAIAAAAAAAAAAAAAAAIAAAACAAAAAAAAAAAAAwcBAQcB" +
        "AQcBAf9SAAwAAAABAAAEBAAB/1wABEBA/2QAJQABQ3JlYXRlZCBieSBPcGVuSlBFRyB2ZXJzaW9uIDIuNS40/5AA" +
        "CgAAAAAAIAAB/5PH1AYOBpTH1AYOCWjPtAwM+D//2Q==";

    // 2x2 CMYK JP2, built the same way with four components and the colr box's EnumCS set to 12.
    // The left column carries no ink at all and the right column is full cyan; K is zero across
    // the image, which is what made the pre-fix decode - K read as alpha - come out invisible.
    private const string CmykJp2Base64 =
        "AAAADGpQICANCocKAAAAFGZ0eXBqcDIgAAAAAGpwMiAAAAAtanAyaAAAABZpaGRyAAAAAgAAAAIABAcHAAAAAAAP" +
        "Y29scgEAAAAAAAwAAAChanAyY/9P/1EAMgAAAAAAAgAAAAIAAAAAAAAAAAAAAAIAAAACAAAAAAAAAAAABAcBAQcB" +
        "AQcBAQcBAf9SAAwAAAABAAAEBAAB/1wABEBA/2QAJQABQ3JlYXRlZCBieSBPcGVuSlBFRyB2ZXJzaW9uIDIuNS40" +
        "/5AACgAAAAAAJgAB/5PfgDAI8SbXRz/fgBAIkN+AEAiQ34AQCJD/2Q==";

    // Load + wire the native wrapper once; false when it (or its deps) can't be found/loaded.
    private static readonly Lazy<bool> NativeJ2KReady = new(TryPrepareNativeJ2K);

    [Fact]
    public void DecodesSyccJp2_ConvertsLumaChromaToRgb()
    {
        using var bitmap = Decode(SyccJp2Base64, "sycc");

        var neutral = bitmap.GetPixel(0, 0);
        Assert.InRange(neutral.Red, 126, 130);
        Assert.InRange(neutral.Green, 126, 130);
        Assert.InRange(neutral.Blue, 126, 130);

        // The discriminating pixel. Read as RGB it is (76, 85, 255), a mid-blue; converted it is
        // very nearly pure red.
        var red = bitmap.GetPixel(1, 0);
        Assert.True(red.Red > 200, $"expected a red pixel, got {red}");
        Assert.True(red.Green < 40, $"expected a red pixel, got {red}");
        Assert.True(red.Blue < 40, $"expected a red pixel, got {red}");
        Assert.Equal(255, red.Alpha);
    }

    [Fact]
    public void DecodesCmykJp2_InvertsInkAndKeepsBlackOutOfAlpha()
    {
        using var bitmap = Decode(CmykJp2Base64, "cmyk");

        var blank = bitmap.GetPixel(0, 0);
        Assert.True(blank.Red > 240 && blank.Green > 240 && blank.Blue > 240, $"expected white, got {blank}");
        Assert.Equal(255, blank.Alpha);

        var cyan = bitmap.GetPixel(1, 0);
        Assert.True(cyan.Red < 20, $"expected cyan, got {cyan}");
        Assert.True(cyan.Green > 240, $"expected cyan, got {cyan}");
        Assert.True(cyan.Blue > 240, $"expected cyan, got {cyan}");
        Assert.Equal(255, cyan.Alpha);
    }

    private static SKBitmap Decode(string base64, string label)
    {
        if (!NativeJ2KReady.Value)
            Assert.Skip("libj2k_native not available (native wrappers not built for this platform).");

        var tempPath = Path.Combine(Path.GetTempPath(), $"lyra-{label}-{Guid.NewGuid():N}.jp2");
        File.WriteAllBytes(tempPath, Convert.FromBase64String(base64));

        try
        {
            using var composite = new Composite(new FileInfo(tempPath));
            new J2KDecoder().DecodeAsync(composite, CancellationToken.None).GetAwaiter().GetResult();

            var raster = Assert.IsType<RasterContent>(composite.Content);
            using var image = raster.Image;

            Assert.Equal(2, image.Width);
            Assert.Equal(2, image.Height);
            return SKBitmap.FromImage(image);
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

    private static bool TryPrepareNativeJ2K()
    {
        var dylibPath = LocateJ2KNative();
        if (dylibPath is null)
            return false;

        try
        {
            var handle = NativeLibrary.Load(dylibPath);
            try
            {
                NativeLibrary.SetDllImportResolver(typeof(J2KDecoder).Assembly, (name, _, _) =>
                    name is "libj2k_native" or "libj2k_native.dll" or "libj2k_native.so" or "libj2k_native.dylib" ? handle : IntPtr.Zero);
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

    private static string? LocateJ2KNative()
    {
        var (leaf, distDir) = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? ("libj2k_native.dll", "dist-windows")
            : RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                ? ("libj2k_native.so", "dist-linux")
                : ("libj2k_native.dylib", "dist-macos");

        var relDir = Path.Combine("release", "native", distDir);

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, relDir, leaf);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }
}