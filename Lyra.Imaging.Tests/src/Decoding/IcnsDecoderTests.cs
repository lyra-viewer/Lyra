using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Decoders;
using Lyra.ManagedCodecs.Raster.Icns;
using SkiaSharp;
using Xunit;

namespace Lyra.Imaging.Tests.Decoding;

/// <summary>
/// Covers the .icns container reader and the decoder that turns it into selectable variants.
/// </summary>
public class IcnsDecoderTests
{
    // ------------------------------------------------------------------
    //  Container walk
    // ------------------------------------------------------------------

    [Fact]
    public void ReadEntries_RejectsNonIcns()
    {
        Assert.Empty(IcnsReader.ReadEntries("not an icns file at all"u8.ToArray()));
        Assert.Empty(IcnsReader.ReadEntries([]));
    }

    [Fact]
    public void ReadEntries_OrdersLargestFirst()
    {
        var icns = Container(
            Chunk("is32", Rle24Plate(16)),
            Chunk("s8mk", new byte[16 * 16]),
            Chunk("il32", Rle24Plate(32)),
            Chunk("l8mk", new byte[32 * 32])
        );

        var entries = IcnsReader.ReadEntries(icns);

        Assert.Equal(["il32", "is32"], entries.Select(e => e.Type.Code));
    }

    [Fact]
    public void ReadEntries_SkipsMaskAndMetadataChunks()
    {
        var icns = Container(
            Chunk("TOC ", new byte[8]),
            Chunk("icnV", [0, 0, 0, 0]),
            Chunk("is32", Rle24Plate(16)),
            Chunk("s8mk", new byte[16 * 16])
        );

        var entries = IcnsReader.ReadEntries(icns);

        Assert.Equal("is32", Assert.Single(entries).Type.Code);
    }

    [Fact]
    public void ReadEntries_StopsAtChunkRunningPastTheContainer()
    {
        var icns = Container(Chunk("is32", Rle24Plate(16)));

        // Claim a chunk length far beyond the buffer - the walk must stop, not read past it.
        icns[8 + 4] = 0x7F;

        Assert.Empty(IcnsReader.ReadEntries(icns));
    }

    [Fact]
    public void ReadEntries_SurvivesChunkLengthShorterThanItsOwnHeader()
    {
        var icns = Container(Chunk("is32", Rle24Plate(16)));
        
        // length = 2, smaller than the 8-byte chunk header
        icns[8 + 7] = 0x02; 

        Assert.Empty(IcnsReader.ReadEntries(icns));
    }

    // ------------------------------------------------------------------
    //  Pixel layouts
    // ------------------------------------------------------------------

    [Fact]
    public void Decode_Rle24_AppliesTheSeparateMask()
    {
        // A 16x16 plate of solid red, with the left half of the mask transparent.
        var mask = new byte[16 * 16];
        for (var y = 0; y < 16; y++)
        for (var x = 8; x < 16; x++)
            mask[(y * 16) + x] = 0xFF;

        var icns = Container(
            Chunk("is32", Rle24Plate(16, r: 0xFF, g: 0x00, b: 0x00)),
            Chunk("s8mk", mask)
        );

        var entry = Assert.Single(IcnsReader.ReadEntries(icns));
        var decoded = IcnsReader.Decode(icns, entry);

        Assert.NotNull(decoded);
        Assert.Equal(16, decoded!.Value.Width);

        var pixels = decoded.Value.Pixels;
        Assert.Equal(0xFF, pixels[0]);           // red channel present across the whole plate
        Assert.Equal(0x00, pixels[3]);           // ...but the left half is masked out
        Assert.Equal(0xFF, pixels[(8 * 4) + 3]); // and the right half is opaque
    }

    [Fact]
    public void Decode_Rle24_WithoutMask_FallsBackToOpaque()
    {
        var icns = Container(Chunk("is32", Rle24Plate(16, r: 0x20, g: 0x40, b: 0x60)));

        var entry = Assert.Single(IcnsReader.ReadEntries(icns));
        var decoded = IcnsReader.Decode(icns, entry);

        Assert.NotNull(decoded);
        Assert.Equal(0x20, decoded!.Value.Pixels[0]);
        Assert.Equal(0x40, decoded.Value.Pixels[1]);
        Assert.Equal(0x60, decoded.Value.Pixels[2]);
        Assert.Equal(0xFF, decoded.Value.Pixels[3]);
    }

    [Fact]
    public void Decode_Argb_ReadsAllFourPlanes()
    {
        var payload = new List<byte>();
        payload.AddRange("ARGB"u8.ToArray());
        payload.AddRange(RunPlane(16 * 16, 0x80)); // A
        payload.AddRange(RunPlane(16 * 16, 0x11)); // R
        payload.AddRange(RunPlane(16 * 16, 0x22)); // G
        payload.AddRange(RunPlane(16 * 16, 0x33)); // B

        var icns = Container(Chunk("ic04", payload.ToArray()));

        var entry = Assert.Single(IcnsReader.ReadEntries(icns));
        Assert.Equal(IcnsPayloadKind.Argb, entry.Kind);

        var decoded = IcnsReader.Decode(icns, entry);

        Assert.NotNull(decoded);
        Assert.Equal([0x11, 0x22, 0x33, 0x80], decoded!.Value.Pixels[..4]);
    }

    [Fact]
    public void Decode_TruncatedPlane_ReturnsNullRatherThanPartialPixels()
    {
        var icns = Container(Chunk("is32", [0xFF]));

        var entry = Assert.Single(IcnsReader.ReadEntries(icns));

        Assert.Null(IcnsReader.Decode(icns, entry));
    }

    // ------------------------------------------------------------------
    //  Decoder + variants
    // ------------------------------------------------------------------

    [Fact]
    public void Decoder_PublishesOneVariantPerIcon_LargestActive()
    {
        var icns = Container(
            Chunk("is32", Rle24Plate(16)),
            Chunk("s8mk", new byte[16 * 16]),
            Chunk("il32", Rle24Plate(32)),
            Chunk("l8mk", new byte[32 * 32]),
            Chunk("ic07", PngBytes(128))
        );

        var path = Path.Combine(Path.GetTempPath(), $"lyra-icns-{Guid.NewGuid():N}.icns");
        File.WriteAllBytes(path, icns);

        try
        {
            using var composite = new Composite(new FileInfo(path));
            new IcnsDecoder().DecodeAsync(composite, CancellationToken.None).GetAwaiter().GetResult();

            var set = Assert.IsType<VariantRasterContent>(composite.Content);
            Assert.Equal(["128 x 128", "32 x 32", "16 x 16"], set.Variants.Select(v => v.Label));

            // Opens on the largest, and Content tracks the selection.
            Assert.Equal(0, set.ActiveIndex);
            Assert.Equal(128f, composite.Content!.DecodedWidth);

            Assert.True(set.Select(2));
            Assert.Equal(2, set.ActiveIndex);
            Assert.Equal(16f, composite.Content!.DecodedWidth);

            // Reselecting the same variant is a no-op the UI can skip.
            Assert.False(set.Select(2));
            Assert.False(set.Select(99));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Decoder_DescribesTheContainer_InFormatSpecific()
    {
        var icns = Container(
            Chunk("is32", Rle24Plate(16)),
            Chunk("s8mk", new byte[16 * 16]),
            Chunk("ic07", PngBytes(128)),
            Chunk("ic11", PngBytes(32))
        );

        var path = Path.Combine(Path.GetTempPath(), $"lyra-icns-{Guid.NewGuid():N}.icns");
        File.WriteAllBytes(path, icns);

        try
        {
            using var composite = new Composite(new FileInfo(path));
            new IcnsDecoder().DecodeAsync(composite, CancellationToken.None).GetAwaiter().GetResult();

            var facts = composite.FormatSpecificSnapshot().ToDictionary(p => p.Key, p => p.Value);

            Assert.Equal("3", facts["Icons"]);
            Assert.Equal("16 x 16 to 128 x 128", facts["Size Range"]);

            // Encodings are sniffed from the payloads and ordered by how many use each.
            Assert.Equal("PNG x2, RLE24", facts["Encodings"]);

            // Apple type codes, so the panel says which entries the container actually carries.
            Assert.Equal("ic07, ic11, is32", facts["Types"]);

            // ic11 is the only @2x entry here.
            Assert.Equal("1 of 3", facts["Retina Entries"]);

            Assert.Contains(" of ", facts["Icon Data"]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Decoder_SwitchingVariants_LeavesEveryVariantUsable()
    {
        var icns = Container(
            Chunk("il32", Rle24Plate(32)),
            Chunk("l8mk", new byte[32 * 32]),
            Chunk("ic07", PngBytes(128))
        );

        var path = Path.Combine(Path.GetTempPath(), $"lyra-icns-{Guid.NewGuid():N}.icns");
        File.WriteAllBytes(path, icns);

        try
        {
            using var composite = new Composite(new FileInfo(path));
            new IcnsDecoder().DecodeAsync(composite, CancellationToken.None).GetAwaiter().GetResult();

            var set = Assert.IsType<VariantRasterContent>(composite.Content);
            var held = ((RasterContent)set.Active).Image;

            set.Select(1);

            // Still readable after the swap - nothing was disposed underneath it.
            Assert.NotEqual(IntPtr.Zero, held.Handle);
            Assert.Equal(128, held.Width);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Variants are ordered by the size that actually decoded, not by the size the type code
    /// claims.
    /// </summary>
    [Fact]
    public void Decoder_OrdersByDecodedSize_NotByWhatTheTypeCodeClaims()
    {
        var icns = Container(
            Chunk("ic09", PngBytes(64)),   // claims 512x512, holds 64x64
            Chunk("ic07", PngBytes(512))   // claims 128x128, holds 512x512
        );

        var path = Path.Combine(Path.GetTempPath(), $"lyra-icns-{Guid.NewGuid():N}.icns");
        File.WriteAllBytes(path, icns);

        try
        {
            using var composite = new Composite(new FileInfo(path));
            new IcnsDecoder().DecodeAsync(composite, CancellationToken.None).GetAwaiter().GetResult();

            var set = Assert.IsType<VariantRasterContent>(composite.Content);

            Assert.Equal(["512 x 512", "64 x 64"], set.Variants.Select(v => v.Label));
            Assert.Equal(512f, composite.Content!.DecodedWidth);

            // The reported range is the real one too.
            var facts = composite.FormatSpecificSnapshot().ToDictionary(p => p.Key, p => p.Value);
            Assert.Equal("64 x 64 to 512 x 512", facts["Size Range"]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ------------------------------------------------------------------
    //  Builders
    // ------------------------------------------------------------------

    private static byte[] Container(params byte[][] chunks)
    {
        var body = chunks.SelectMany(c => c).ToArray();
        var total = 8 + body.Length;

        var result = new byte[total];
        "icns"u8.CopyTo(result);
        WriteUInt32BigEndian(result, 4, (uint)total);
        body.CopyTo(result, 8);

        return result;
    }

    private static byte[] Chunk(string code, byte[] payload)
    {
        var result = new byte[8 + payload.Length];
        System.Text.Encoding.ASCII.GetBytes(code).CopyTo(result, 0);
        WriteUInt32BigEndian(result, 4, (uint)result.Length);
        payload.CopyTo(result, 8);
        return result;
    }

    private static void WriteUInt32BigEndian(byte[] target, int offset, uint value)
    {
        target[offset] = (byte)(value >> 24);
        target[offset + 1] = (byte)(value >> 16);
        target[offset + 2] = (byte)(value >> 8);
        target[offset + 3] = (byte)value;
    }

    /// <summary>Three RLE-compressed constant planes, the layout an is32/il32 chunk holds.</summary>
    private static byte[] Rle24Plate(int size, byte r = 0x10, byte g = 0x20, byte b = 0x30)
    {
        var count = size * size;
        return [.. RunPlane(count, r), .. RunPlane(count, g), .. RunPlane(count, b)];
    }

    /// <summary>One plane of <paramref name="count"/> copies of <paramref name="value"/>, run-encoded.</summary>
    private static byte[] RunPlane(int count, byte value)
    {
        var bytes = new List<byte>();
        var remaining = count;

        while (remaining > 0)
        {
            // Runs encode 3..130 bytes as (0x80 | length - 3), value.
            var run = Math.Min(remaining, 130);
            bytes.Add((byte)(0x80 + run - 3));
            bytes.Add(value);
            remaining -= run;
        }

        return bytes.ToArray();
    }

    private static byte[] PngBytes(int size)
    {
        var info = new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);
        bitmap.Erase(new SKColor(0x40, 0x80, 0xC0, 0xFF));

        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);

        return encoded.ToArray();
    }
}