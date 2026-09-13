using System.Runtime.InteropServices;
using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Decoders;
using SkiaSharp;
using Xunit;

namespace Lyra.Imaging.Tests.Decoding;

public class ExrGrayscaleTests
{
    // 4x2 uncompressed EXR with a single 32-bit float channel named "R", holding the ramp
    // 0.00 0.25 0.50 1.00 / 1.00 0.50 0.25 0.00. The ramp (rather than a flat fill) keeps
    // the test honest about the broadcast copying real pixel data into G and B.
    private const string SingleChannelExrBase64 =
        "di8xAQIAAABjaGFubmVscwBjaGxpc3QAEwAAAFIAAgAAAAAAAAABAAAAAQAAAABjb21wcmVzc2lvbgBjb21wcmVzc2" +
        "lvbgABAAAAAGRhdGFXaW5kb3cAYm94MmkAEAAAAAAAAAAAAAAAAwAAAAEAAABkaXNwbGF5V2luZG93AGJveDJpABAA" +
        "AAAAAAAAAAAAAAMAAAABAAAAbGluZU9yZGVyAGxpbmVPcmRlcgABAAAAAHBpeGVsQXNwZWN0UmF0aW8AZmxvYXQABA" +
        "AAAAAAgD9zY3JlZW5XaW5kb3dDZW50ZXIAdjJmAAgAAAAAAAAAAAAAAHNjcmVlbldpbmRvd1dpZHRoAGZsb2F0AAQA" +
        "AAAAAIA/ACUBAAAAAAAAPQEAAAAAAAAAAAAAEAAAAAAAAAAAAIA+AAAAPwAAgD8BAAAAEAAAAAAAgD8AAAA/AACAPg" +
        "AAAAA=";

    // 4x2 uncompressed EXR with half-float R, G, B and A channels and no chromaticities
    // attribute - the other side of every fact the single-channel file exercises.
    private const string HalfRgbaExrBase64 =
        "di8xAQIAAABjaGFubmVscwBjaGxpc3QASQAAAEEAAQAAAAAAAAABAAAAAQAAAEIAAQAAAAAAAAABAAAAAQAAAEcAAQ" +
        "AAAAAAAAABAAAAAQAAAFIAAQAAAAAAAAABAAAAAQAAAABjb21wcmVzc2lvbgBjb21wcmVzc2lvbgABAAAAAGRhdGFX" +
        "aW5kb3cAYm94MmkAEAAAAAAAAAAAAAAAAwAAAAEAAABkaXNwbGF5V2luZG93AGJveDJpABAAAAAAAAAAAAAAAAMAAA" +
        "ABAAAAbGluZU9yZGVyAGxpbmVPcmRlcgABAAAAAHBpeGVsQXNwZWN0UmF0aW8AZmxvYXQABAAAAAAAgD9zY3JlZW5X" +
        "aW5kb3dDZW50ZXIAdjJmAAgAAAAAAAAAAAAAAHNjcmVlbldpbmRvd1dpZHRoAGZsb2F0AAQAAAAAAIA/AFsBAAAAAA" +
        "AAgwEAAAAAAAAAAAAAIAAAAAA4ADgAOAA4Zi5mLmYuZi5mNmY2ZjZmNmY6ZjpmOmY6AQAAACAAAAAAOAA4ADgAOGYu" +
        "Zi5mLmYuZjZmNmY2ZjZmOmY6ZjpmOg==";

    // 4x2 uncompressed EXR whose single "R" channel is subsampled 2x horizontally, so the file
    // stores two columns per row (0.20 and 0.80) that must be spread across four. RgbaInputFile
    // cannot read this at all - it throws on the sampling mismatch - which is why the single-channel
    // path handles sampling itself rather than handing such files back to it.
    private const string SubsampledSingleChannelExrBase64 =
        "di8xAQIAAABjaGFubmVscwBjaGxpc3QAEwAAAFIAAgAAAAAAAAACAAAAAQAAAABjb21wcmVzc2lvbgBjb21wcmVzc2lvbgAB" +
        "AAAAAGRhdGFXaW5kb3cAYm94MmkAEAAAAAAAAAAAAAAAAwAAAAEAAABkaXNwbGF5V2luZG93AGJveDJpABAAAAAAAAAAAAAA" +
        "AAMAAAABAAAAbGluZU9yZGVyAGxpbmVPcmRlcgABAAAAAHBpeGVsQXNwZWN0UmF0aW8AZmxvYXQABAAAAAAAgD9zY3JlZW5X" +
        "aW5kb3dDZW50ZXIAdjJmAAgAAAAAAAAAAAAAAHNjcmVlbldpbmRvd1dpZHRoAGZsb2F0AAQAAAAAAIA/ACUBAAAAAAAANQEA" +
        "AAAAAAAAAAAACAAAAM3MTD7NzEw/AQAAAAgAAADNzEw+zcxMPw==";

    [Fact]
    public void DecodesSubsampledSingleChannelExr_SpreadingItAcrossThePixelsItStandsFor()
    {
        Decode(SubsampledSingleChannelExrBase64, (bitmap, _) =>
        {
            Assert.Equal(4, bitmap.Width);
            Assert.Equal(2, bitmap.Height);

            for (var y = 0; y < bitmap.Height; y++)
            {
                var left = bitmap.GetPixel(0, y);
                var right = bitmap.GetPixel(2, y);

                // Neutral, not a red-only plate.
                Assert.True(left.Red == left.Green && left.Green == left.Blue, $"({0},{y}) is not neutral: {left}");

                // Each stored sample covers two columns - a wrong stride shows up here first.
                Assert.Equal(left, bitmap.GetPixel(1, y));
                Assert.Equal(right, bitmap.GetPixel(3, y));

                // And the two samples really are different, so this is not a uniform fill.
                Assert.True(right.Red > left.Red, $"row {y}: right sample {right.Red} is not brighter than left {left.Red}");
            }
        });
    }

    private static readonly Lazy<bool> NativeExrReady = new(TryPrepareNativeExr);

    [Fact]
    public void DecodesSingleChannelExr_AsGrayscale_NotRedOnly()
    {
        Decode(SingleChannelExrBase64, (bitmap, formatSpecific) =>
        {
            Assert.Equal(4, bitmap.Width);
            Assert.Equal(2, bitmap.Height);

            // 1. Every pixel is neutral and opaque - the red-only plate is gone.
            for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                Assert.True(pixel.Red == pixel.Green && pixel.Green == pixel.Blue, $"pixel ({x},{y}) is not neutral: {pixel}");
                Assert.Equal(255, pixel.Alpha);
            }

            // 2. The ramp survived, so the broadcast carries the source data rather than
            //    flattening it (and the rows aren't transposed or reversed).
            Assert.True(bitmap.GetPixel(0, 0).Red < bitmap.GetPixel(3, 0).Red);
            Assert.True(bitmap.GetPixel(0, 1).Red > bitmap.GetPixel(3, 1).Red);

            // 3. And it is reported as grayscale in the metadata panel.
            Assert.Contains(new KeyValuePair<string, string>("GrayScale", "True"), formatSpecific);
        });
    }

    [Fact]
    public void PublishesHeaderFacts_ForSingleChannelExr()
    {
        Decode(SingleChannelExrBase64, (_, formatSpecific) =>
        {
            Assert.Contains(new KeyValuePair<string, string>("Bit Depth", "32-bit float"), formatSpecific);
            Assert.Contains(new KeyValuePair<string, string>("Alpha", "False"), formatSpecific);
            Assert.Contains(new KeyValuePair<string, string>("Color Space", "Linear Gray"), formatSpecific);
        });
    }

    [Fact]
    public void PublishesHeaderFacts_ForHalfFloatRgbaExr()
    {
        Decode(HalfRgbaExrBase64, (_, formatSpecific) =>
        {
            // Sample format comes from the color channels, and a file with no chromaticities
            // attribute is Rec.709 by definition rather than "custom".
            Assert.Contains(new KeyValuePair<string, string>("Bit Depth", "16-bit float"), formatSpecific);
            Assert.Contains(new KeyValuePair<string, string>("Alpha", "True"), formatSpecific);
            Assert.Contains(new KeyValuePair<string, string>("Color Space", "Linear Rec.709"), formatSpecific);
            Assert.Contains(new KeyValuePair<string, string>("GrayScale", "False"), formatSpecific);
        });
    }

    private static void Decode(string base64, Action<SKBitmap, List<KeyValuePair<string, string>>> assert)
    {
        if (!NativeExrReady.Value)
            Assert.Skip("libexr_native not available (native wrappers not built for this platform).");

        var tempPath = Path.Combine(Path.GetTempPath(), $"lyra-exr-{Guid.NewGuid():N}.exr");
        File.WriteAllBytes(tempPath, Convert.FromBase64String(base64));

        try
        {
            using var composite = new Composite(new FileInfo(tempPath));
            new ExrDecoder().DecodeAsync(composite, CancellationToken.None).GetAwaiter().GetResult();

            var raster = Assert.IsAssignableFrom<RasterContent>(composite.Content);
            using var bitmap = SKBitmap.FromImage(raster.Image);

            assert(bitmap, composite.FormatSpecificSnapshot());
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

    private static bool TryPrepareNativeExr()
    {
        var libPath = LocateExrNative();
        if (libPath is null)
            return false;

        try
        {
            var handle = NativeLibrary.Load(libPath);
            try
            {
                NativeLibrary.SetDllImportResolver(typeof(ExrDecoder).Assembly, (name, _, _) =>
                    name is "libexr_native" or "libexr_native.dll" or "libexr_native.so"
                        or "libexr_native.dylib" or "libexr"
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
    
    private static string? LocateExrNative()
    {
        var (leaf, distDir) =
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? ("libexr_native.dll", "dist-windows")
                : RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                    ? ("libexr_native.so", "dist-linux")
                    : ("libexr_native.dylib", "dist-macos");

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
