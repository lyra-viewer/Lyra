using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Decoders;
using Lyra.Imaging.Tests.Support;
using Lyra.ManagedCodecs.Tests.Heif;
using SkiaSharp;
using Xunit;

namespace Lyra.Imaging.Tests.Decoding;

/// <summary>
/// AVIF and HEIF image sequences through libheif. The library comes from the system - Homebrew
/// or the distribution - so these skip where it cannot be found.
/// </summary>
public class HeifSequenceTests
{
    private const uint Red = 0xFFFF0000, Green = 0xFF00FF00, Blue = 0xFF0000FF;

    private static readonly string[] LibheifCandidates = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? ["libheif.dll", "heif.dll"]
        : RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            ? ["libheif.so.1", "libheif.so"]
            : ["/opt/homebrew/opt/libheif/lib/libheif.dylib", "/usr/local/opt/libheif/lib/libheif.dylib", "libheif.dylib"];

    private static readonly Lazy<bool> LibheifReady = new(() =>
        NativeWrapper.TryLoadSystem("libheif", LibheifCandidates, typeof(HeifDecoder).Assembly));

    [Fact]
    public void AnAnimatedAvif_IsPublishedAsFrames()
    {
        WithDecoded(HeifSequenceFixtures.Avif, composite =>
        {
            var set = Assert.IsType<VariantRasterContent>(composite.Content);

            Assert.Equal(VariantKind.Frames, set.Kind);
            Assert.Equal(["Frame 1", "Frame 2", "Frame 3"], set.Variants.Select(v => v.Label));
            Assert.Equal(["16x12, 100 ms", "16x12, 200 ms", "16x12, 50 ms"], set.Variants.Select(v => v.Detail));
            Assert.All(set.Variants, v => Assert.True(v.ByteSize > 0));

            var facts = Facts(composite);
            Assert.Equal("AV1", facts["Codec"]);
            Assert.Equal("3", facts["Frames"]);
            Assert.Equal("350 ms", facts["Duration"]);
            Assert.Equal("forever", facts["Loop"]);
        });
    }

    [Fact]
    public void Frames_DecodeInAnyOrder()
    {
        WithDecoded(HeifSequenceFixtures.Avif, composite =>
        {
            var set = Assert.IsType<VariantRasterContent>(composite.Content);

            Assert.Equal(0, PixelOf(set.Active, 2, 2).Alpha);
            Assert.Equal(Red, (uint)PixelOf(set.Active, 12, 6));

            foreach (var (index, x, y, expected) in new[] { (2, 3, 5, Blue), (1, 3, 5, Green), (1, 12, 6, Red), (0, 12, 6, Red), (2, 0, 0, Blue) })
            {
                Assert.True(SelectAndWait(set, index), $"frame {index + 1} did not arrive");
                Assert.Equal(expected, (uint)PixelOf(set.Active, x, y));
            }
        });
    }

    [Fact]
    public void AnAlphaTrackLibheifDoesNotMerge_IsMergedHere()
    {
        WithDecoded(HeifSequenceFixtures.AvifOldAlpha, composite =>
        {
            var set = Assert.IsType<VariantRasterContent>(composite.Content);

            Assert.Equal(0, PixelOf(set.Active, 2, 2).Alpha);
            Assert.Equal(Red, (uint)PixelOf(set.Active, 12, 6));

            Assert.True(SelectAndWait(set, 2));
            Assert.Equal(Blue, (uint)PixelOf(set.Active, 2, 2));
        });
    }

    [Fact]
    public void ThePlayCount_IsRead()
    {
        WithDecoded(HeifSequenceFixtures.AvifThreePlays, composite =>
        {
            var set = Assert.IsType<VariantRasterContent>(composite.Content);

            Assert.Equal(["16x12, 10 ms", "16x12, 10 ms"], set.Variants.Select(v => v.Detail));
            Assert.Equal("2 repeats", Facts(composite)["Loop"]);
        });
    }

    [Fact]
    public void AnHevcSequence_IsCroppedToItsTrack()
    {
        WithDecoded(HeifSequenceFixtures.Hevc, composite =>
        {
            var set = Assert.IsType<VariantRasterContent>(composite.Content);

            Assert.All(set.Variants, v => Assert.Equal((64, 48), (v.Width, v.Height)));
            Assert.Equal("HEVC", Facts(composite)["Codec"]);

            Assert.True(SelectAndWait(set, 2));
            var raster = Assert.IsType<RasterContent>(set.Active);
            Assert.Equal((64, 48), (raster.Image.Width, raster.Image.Height));

            var pixel = PixelOf(raster, 40, 47);
            Assert.True(pixel.Blue > 240 && pixel.Red < 16, $"expected blue, got {pixel}");
        });
    }

    [Fact]
    public void AnEditListStartingMidway_ShowsTheStillImage()
    {
        var file = Convert.FromBase64String(HeifSequenceFixtures.Avif);
        StartEditsAt(file, mediaTime: 1);

        WithDecoded(file, composite =>
        {
            Assert.IsType<RasterContent>(composite.Content);
            Assert.False(Facts(composite).ContainsKey("Frames"));
        });
    }

    [Fact]
    public void ASequenceThatCannotBeDecoded_ShowsTheStillImage()
    {
        var file = Convert.FromBase64String(HeifSequenceFixtures.Avif);
        MisplaceColorSamples(file);

        WithDecoded(file, composite =>
        {
            var raster = Assert.IsType<RasterContent>(composite.Content);
            Assert.Equal(Red, (uint)PixelOf(raster, 12, 6));

            var facts = Facts(composite);
            Assert.StartsWith("unreadable", facts["Sequence"]);
            Assert.False(facts.ContainsKey("Frames"));
        });
    }

    [Fact]
    public void ASequenceWithNoSize_ShowsTheStillImage()
    {
        var file = Convert.FromBase64String(HeifSequenceFixtures.Avif);
        ZeroTrackSizes(file);

        WithDecoded(file, composite =>
        {
            var raster = Assert.IsType<RasterContent>(composite.Content);
            Assert.Equal(Red, (uint)PixelOf(raster, 12, 6));
            Assert.False(Facts(composite).ContainsKey("Frames"));
        });
    }

    [Fact]
    public void ASequenceOnlyFileWithNoSize_FailsSayingWhy()
    {
        var file = Convert.FromBase64String(HeifSequenceFixtures.Avif);
        ZeroTrackSizes(file);
        DropPrimaryItem(file);

        if (!LibheifReady.Value)
            Assert.Skip("libheif not available on this machine.");

        using var temp = new TempFile(file);
        using var composite = new Composite(new FileInfo(temp.Path));

        var thrown = Assert.Throws<InvalidOperationException>(() => new HeifDecoder().DecodeAsync(composite, CancellationToken.None).GetAwaiter().GetResult());
        Assert.StartsWith("Invalid dimensions", thrown.Message);
    }

    /// <summary>Zeroes the width and height of every track's sample entry, which libheif reports as the sequence's size.</summary>
    private static void ZeroTrackSizes(byte[] file)
    {
        for (var at = file.AsSpan().IndexOf("stsd"u8); at >= 0;)
        {
            // stsd: type, version and flags, entry count, then the entry: size, type, and a visual
            // sample entry whose 16-bit width and height follow 24 bytes of reserved and defaults.
            var dimensions = at + 4 + 4 + 4 + 8 + 24;
            file.AsSpan(dimensions, 4).Clear();

            var next = file.AsSpan(at + 4).IndexOf("stsd"u8);
            at = next < 0 ? -1 : at + 4 + next;
        }
    }

    /// <summary>
    /// Points the color track's samples at the file's header instead of their data. The still image
    /// shares its bytes with the first frame but finds them through its own pointer, so it survives.
    /// </summary>
    private static void MisplaceColorSamples(byte[] file)
    {
        // stco: type, version and flags, entry count, then the first chunk's offset.
        var stco = file.AsSpan().IndexOf("stco"u8);
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(stco + 12), 16);
    }

    [Fact]
    public void ASequenceOnlyFileThatCannotBeShown_SaysWhy()
    {
        var file = Convert.FromBase64String(HeifSequenceFixtures.Avif);
        StartEditsAt(file, mediaTime: 1);
        DropPrimaryItem(file);

        if (!LibheifReady.Value)
            Assert.Skip("libheif not available on this machine.");

        using var temp = new TempFile(file);
        using var composite = new Composite(new FileInfo(temp.Path));

        var thrown = Assert.Throws<LoadFailureException>(() => new HeifDecoder().DecodeAsync(composite, CancellationToken.None).GetAwaiter().GetResult());
        Assert.Equal("The file's image sequence has an edit list that reorders its frames, which is not supported", thrown.Message);
    }

    [Theory]
    [InlineData(nameof(HeifSequenceFixtures.Avif))]
    [InlineData(nameof(HeifSequenceFixtures.AvifOldAlpha))]
    public void ASequenceOnlyFile_IsShownFrameByFrame(string fixture)
    {
        var file = SequenceOnly(fixture);

        WithDecoded(file, composite =>
        {
            var set = Assert.IsType<VariantRasterContent>(composite.Content);
            Assert.Equal(3, set.Variants.Count);
            Assert.Equal(0, PixelOf(set.Active, 2, 2).Alpha);
            Assert.Equal(Red, (uint)PixelOf(set.Active, 12, 6));

            var facts = Facts(composite);
            Assert.Equal("AV1", facts["Codec"]);
            Assert.False(facts.ContainsKey("Has Alpha"));
        });
    }

    [Theory]
    [InlineData(nameof(HeifSequenceFixtures.Avif))]
    [InlineData(nameof(HeifSequenceFixtures.AvifOldAlpha))]
    public void ASequenceOnlyFile_HasItsFirstFrameAsThumbnail(string fixture)
    {
        var file = SequenceOnly(fixture);

        if (!LibheifReady.Value)
            Assert.Skip("libheif not available on this machine.");

        using var temp = new TempFile(file);
        using var thumbnail = new HeifDecoder().DecodeThumbnail(temp.Path, 16, CancellationToken.None);

        Assert.NotNull(thumbnail);
        Assert.Equal((16, 12), (thumbnail.Width, thumbnail.Height));
        Assert.Equal(0, thumbnail.GetPixel(2, 2).Alpha);
        Assert.Equal(Red, (uint)thumbnail.GetPixel(12, 6));
    }

    private static byte[] SequenceOnly(string fixture)
    {
        var base64 = (string)typeof(HeifSequenceFixtures).GetField(fixture)!.GetValue(null)!;
        var file = Convert.FromBase64String(base64);
        DropPrimaryItem(file);
        return file;
    }

    /// <summary>Turns the top-level meta box into a free one, so the file is a sequence and nothing else.</summary>
    private static void DropPrimaryItem(byte[] file)
    {
        for (var at = 0; at + 8 <= file.Length;)
        {
            var size = (int)BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(at));
            if (file.AsSpan(at + 4, 4).SequenceEqual("meta"u8))
            {
                "free"u8.CopyTo(file.AsSpan(at + 4));
                return;
            }

            Assert.True(size >= 8, "the fixture has a box this walk cannot step over");
            at += size;
        }

        Assert.Fail("the fixture has no top-level meta box");
    }

    /// <summary>Points every version-1 elst entry's media time at <paramref name="mediaTime"/>.</summary>
    private static void StartEditsAt(byte[] file, long mediaTime)
    {
        for (var i = 4; i + 4 <= file.Length; i++)
        {
            if (!file.AsSpan(i, 4).SequenceEqual("elst"u8) || file[i + 4] != 1)
                continue;

            var entries = BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(i + 8));
            for (var e = 0; e < entries; e++)
                BinaryPrimitives.WriteInt64BigEndian(file.AsSpan(i + 12 + e * 20 + 8), mediaTime);
        }
    }

    private static void WithDecoded(string base64, Action<Composite> assert) => WithDecoded(Convert.FromBase64String(base64), assert);

    private static void WithDecoded(byte[] file, Action<Composite> assert)
    {
        if (!LibheifReady.Value)
            Assert.Skip("libheif not available on this machine.");

        using var temp = new TempFile(file);
        using var composite = new Composite(new FileInfo(temp.Path));

        new HeifDecoder().DecodeAsync(composite, CancellationToken.None).GetAwaiter().GetResult();

        assert(composite);
    }

    private static Dictionary<string, string> Facts(Composite composite) => composite.FormatSpecificSnapshot().ToDictionary(p => p.Key, p => p.Value);

    private static bool SelectAndWait(VariantRasterContent set, int index)
    {
        if (set.ShownIndex == index)
            return true;

        using var settled = new ManualResetEventSlim(false);
        void Settle(VariantRasterContent _) => settled.Set();

        set.VariantReady += Settle;
        set.VariantFailed += Settle;

        try
        {
            if (!set.Select(index))
                return false;

            return set.ShownIndex == index || (settled.Wait(TimeSpan.FromSeconds(10)) && set.ShownIndex == index);
        }
        finally
        {
            set.VariantReady -= Settle;
            set.VariantFailed -= Settle;
        }
    }

    private static SKColor PixelOf(ICompositeContent? content, int x, int y)
    {
        var raster = Assert.IsAssignableFrom<RasterContent>(content);
        using var bitmap = SKBitmap.FromImage(raster.Image);
        return bitmap.GetPixel(x, y);
    }
}
