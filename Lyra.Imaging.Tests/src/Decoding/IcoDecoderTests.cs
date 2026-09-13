using System.Buffers.Binary;
using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Decoders;
using Lyra.ManagedCodecs.Raster.Ico;
using SkiaSharp;
using Xunit;

namespace Lyra.Imaging.Tests.Decoding;

/// <summary>
/// Covers the .ico container reader and the decoder that turns it into selectable variants.
/// </summary>
public class IcoDecoderTests
{
    [Fact]
    public void ReadEntries_RejectsWhatIsNotAnIcon()
    {
        Assert.Empty(IcoReader.ReadEntries("not an icon at all"u8.ToArray()));
        Assert.Empty(IcoReader.ReadEntries([]));

        // The right length and the wrong reserved word: a BMP starts "BM" and must not be read
        // as a directory of two entries.
        Assert.Empty(IcoReader.ReadEntries([.. "BM"u8, .. new byte[MinimumIco]]));
    }

    [Fact]
    public void ReadEntries_OrdersLargestFirst()
    {
        var ico = Ico((16, Dib32(16, 16, Solid(0x10, 0x20, 0x30))), (48, Dib32(48, 48, Solid(0x10, 0x20, 0x30))), (32, Dib32(32, 32, Solid(0x10, 0x20, 0x30))));

        Assert.Equal([48, 32, 16], IcoReader.ReadEntries(ico).Select(entry => entry.Width));
    }

    /// <summary>
    /// The depth comes from the payload, never from the directory.
    /// </summary>
    [Fact]
    public void ReadEntries_ReadsTheDepthFromThePayload_NotTheDirectory()
    {
        var ico = Ico((16, DibIndexed(16, 16, bits: 4, (_, _) => 1, [(0xFF, 0x00, 0x00), (0x00, 0xFF, 0x00)])));

        // The builder writes a zero depth into the directory, as real files do.
        Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(6 + 6)));
        Assert.Equal(4, Assert.Single(IcoReader.ReadEntries(ico)).BitCount);

        // And the DIB still wins when the directory says something, and says it wrong.
        Declare(ico, 0, bits: 32);
        Assert.Equal(4, Assert.Single(IcoReader.ReadEntries(ico)).BitCount);
    }

    [Fact]
    public void ReadEntries_The256Entry_IsNotReadAsZero()
    {
        var ico = Ico((256, Dib32(256, 256, Solid(0x40, 0x50, 0x60))));

        var entry = Assert.Single(IcoReader.ReadEntries(ico));

        Assert.Equal(256, entry.Width);
        Assert.Equal(256, entry.Height);
    }

    [Fact]
    public void ReadEntries_ReadsAnEmbeddedPngsOwnHeader()
    {
        var ico = Ico((256, PngBytes(256)));

        var entry = Assert.Single(IcoReader.ReadEntries(ico));

        Assert.Equal(IcoPayloadKind.Embedded, entry.Kind);
        Assert.Equal(256, entry.Width);
        Assert.Equal(32, entry.BitCount);
    }

    /// <summary>
    /// A PNG cannot say which rendition it is, so there the directory is believed.
    /// </summary>
    [Fact]
    public void ReadEntries_BelievesTheDirectoryAboutAnEmbeddedPngsDepth()
    {
        var ico = Ico((256, PngBytes(256)), (256, PngBytes(256)), (256, PngBytes(256)));

        Declare(ico, 0, bits: 4);
        Declare(ico, 1, bits: 8);
        Declare(ico, 2, bits: 32);

        // The PNGs are identical RGBA, so nothing but the directory distinguishes them.
        Assert.Equal([32, 8, 4], IcoReader.ReadEntries(ico).Select(entry => entry.BitCount));
    }

    [Fact]
    public void ReadEntries_OrdersEqualEntriesByWeight()
    {
        var ico = Ico((64, PngBytes(64, detail: 8)), (64, PngBytes(64)), (64, PngBytes(64, detail: 2)));

        foreach (var index in new[] { 0, 1, 2 })
            Declare(ico, index, bits: 32);

        var lengths = IcoReader.ReadEntries(ico).Select(entry => entry.PayloadLength).ToList();

        Assert.Equal(3, lengths.Count);
        Assert.Equal(lengths.OrderByDescending(length => length), lengths);
        Assert.True(lengths.Distinct().Count() == 3, $"the payloads need distinct sizes to order: {string.Join(", ", lengths)}");
    }

    [Fact]
    public void ReadEntries_DropsAnEntryPointingPastTheFile()
    {
        var ico = Ico((16, Dib32(16, 16, Solid(1, 2, 3))));

        // Push the payload offset beyond the buffer; the entry cannot be read and must not be
        // reported as though it could.
        BinaryPrimitives.WriteUInt32LittleEndian(ico.AsSpan(6 + 12), (uint)ico.Length + 1024);

        Assert.Empty(IcoReader.ReadEntries(ico));
    }

    [Fact]
    public void ReadEntries_SurvivesALengthThatWouldWrap()
    {
        var ico = Ico((16, Dib32(16, 16, Solid(1, 2, 3))));

        BinaryPrimitives.WriteUInt32LittleEndian(ico.AsSpan(6 + 8), uint.MaxValue - 8);

        Assert.Empty(IcoReader.ReadEntries(ico));
    }

    [Fact]
    public void ReadEntries_DropsAnEntryWhoseDirectoryPromisesMoreThanTheFileHolds()
    {
        var ico = Ico((16, Dib32(16, 16, Solid(1, 2, 3))));

        // Claim four entries where one was written.
        BinaryPrimitives.WriteUInt16LittleEndian(ico.AsSpan(4), 4);

        // The first is still readable; the walk stops when the directory runs off the end.
        Assert.Single(IcoReader.ReadEntries(ico));
    }

    /// <summary>
    /// BI_BITFIELDS and the other compressions put masks, or a whole encoded image, where the
    /// pixels are expected. Icons do not use them, and reading one as though it were BI_RGB would
    /// produce color from arbitrary bytes.
    /// </summary>
    [Fact]
    public void ReadEntries_DeclinesACompressedDib()
    {
        var payload = Dib32(16, 16, Solid(1, 2, 3));
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(16), 3); // BI_BITFIELDS

        Assert.Empty(IcoReader.ReadEntries(Ico((16, payload))));
    }

    [Fact]
    public void Decode_RowsAreStoredBottomUp()
    {
        var ico = Ico((2, Dib32(2, 2, (_, y) => y == 0
            ? ((byte)0xFF, (byte)0, (byte)0, (byte)0xFF)
            : ((byte)0, (byte)0, (byte)0xFF, (byte)0xFF))));

        var image = Decode(ico);

        Assert.Equal(new SKColor(0xFF, 0x00, 0x00, 0xFF), PixelAt(image, 0, 0));
        Assert.Equal(new SKColor(0x00, 0x00, 0xFF, 0xFF), PixelAt(image, 0, 1));
    }

    [Fact]
    public void Decode_32Bit_KeepsItsOwnAlpha()
    {
        var ico = Ico((2, Dib32(2, 2, (x, _) => (0x20, 0x40, 0x60, x == 0 ? (byte)0x80 : (byte)0xFF))));

        var image = Decode(ico);

        Assert.Equal(0x80, PixelAt(image, 0, 0).Alpha);
        Assert.Equal(0xFF, PixelAt(image, 1, 0).Alpha);
        Assert.Equal(new SKColor(0x20, 0x40, 0x60, 0x80), PixelAt(image, 0, 0));
    }

    [Fact]
    public void Decode_32BitWithNoAlphaAtAll_FallsBackToTheMask()
    {
        var ico = Ico((2, Dib32(2, 2, pixel: (_, _) => (0x30, 0x60, 0x90, 0x00), transparent: (x, _) => x == 1)));

        var image = Decode(ico);

        Assert.Equal(0xFF, PixelAt(image, 0, 0).Alpha);
        Assert.Equal(0x00, PixelAt(image, 1, 0).Alpha);
        Assert.Equal(new SKColor(0x30, 0x60, 0x90, 0xFF), PixelAt(image, 0, 0));
    }

    [Fact]
    public void Decode_TheMaskIsTheTransparencyForShallowerEntries()
    {
        var ico = Ico((2, DibIndexed(2, 2, bits: 8, (x, _) => x, [(0xFF, 0x00, 0x00), (0x00, 0xFF, 0x00)], transparent: (x, _) => x == 1)));

        var image = Decode(ico);

        Assert.Equal(new SKColor(0xFF, 0x00, 0x00, 0xFF), PixelAt(image, 0, 0));
        Assert.Equal(0x00, PixelAt(image, 1, 0).Alpha);
    }

    [Fact]
    public void Decode_PaletteEntriesAreStoredBlueFirst()
    {
        var ico = Ico((2, DibIndexed(2, 2, bits: 8, (_, _) => 0, [(0xC0, 0x30, 0x10)])));

        Assert.Equal(new SKColor(0xC0, 0x30, 0x10, 0xFF), PixelAt(Decode(ico), 0, 0));
    }

    /// <summary>
    /// Sub-byte depths pack several pixels into one byte, most significant first. Unpacked the
    /// other way round, a 4-bit icon comes out with its colors swapped in pairs.
    /// </summary>
    [Theory]
    [InlineData(4)]
    [InlineData(1)]
    public void Decode_SubBytePixelsUnpackHighBitsFirst(ushort bits)
    {
        var ico = Ico((2, DibIndexed(2, 2, bits, (x, _) => x, [(0xFF, 0x00, 0x00), (0x00, 0x00, 0xFF)])));

        var image = Decode(ico);

        Assert.Equal(new SKColor(0xFF, 0x00, 0x00, 0xFF), PixelAt(image, 0, 0));
        Assert.Equal(new SKColor(0x00, 0x00, 0xFF, 0xFF), PixelAt(image, 1, 0));
    }

    /// <summary>
    /// At 16 bits a DIB stores X1R5G5B5, which is five bits per channel and one ignored. Read as
    /// though it were R5G6B5 - the other common 16-bit layout - every color shifts.
    /// </summary>
    [Fact]
    public void Decode_16BitIsFiveBitsPerChannel()
    {
        // Pure red, pure green, pure blue, white.
        var ico = Ico((2, Dib16(2, 2, (x, y) => (x, y) switch
        {
            (0, 0) => 0x7C00,
            (1, 0) => 0x03E0,
            (0, 1) => 0x001F,
            _ => 0x7FFF
        })));

        var image = Decode(ico);

        Assert.Equal(new SKColor(0xFF, 0x00, 0x00, 0xFF), PixelAt(image, 0, 0));
        Assert.Equal(new SKColor(0x00, 0xFF, 0x00, 0xFF), PixelAt(image, 1, 0));
        Assert.Equal(new SKColor(0x00, 0x00, 0xFF, 0xFF), PixelAt(image, 0, 1));
        Assert.Equal(new SKColor(0xFF, 0xFF, 0xFF, 0xFF), PixelAt(image, 1, 1));
    }

    [Fact]
    public void Decode_RowPaddingIsNotMistakenForPixels()
    {
        var ico = Ico((3, DibIndexed(3, 3, bits: 8, (x, y) => x == y ? 1 : 0, [(0x00, 0x00, 0x00), (0xFF, 0xFF, 0xFF)])));

        var image = Decode(ico);

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(new SKColor(0xFF, 0xFF, 0xFF, 0xFF), PixelAt(image, i, i));
            Assert.Equal(new SKColor(0x00, 0x00, 0x00, 0xFF), PixelAt(image, (i + 1) % 3, i));
        }
    }

    [Fact]
    public void Decode_PublishesEveryEntryAsAVariant()
    {
        var ico = Ico((16, Dib32(16, 16, Solid(1, 2, 3))), (32, Dib32(32, 32, Solid(1, 2, 3))), (256, PngBytes(256)));

        WithDecoded(ico, composite =>
        {
            var set = Assert.IsType<VariantRasterContent>(composite.Content);

            Assert.Equal(["256 x 256", "32 x 32", "16 x 16"], set.Variants.Select(variant => variant.Label));
            Assert.Equal(0, set.ActiveIndex);
            Assert.Equal(256f, composite.Content!.DecodedWidth);
        });
    }

    /// <summary>
    /// Two entries of the same size differing only in depth is ordinary in an icon, so the size
    /// alone cannot identify a row - the detail has to carry the rest.
    /// </summary>
    [Fact]
    public void Decode_TellsApartTwoEntriesOfTheSameSize()
    {
        var ico = Ico((16, DibIndexed(16, 16, bits: 4, (_, _) => 1, [(0, 0, 0), (0xFF, 0, 0)])), (16, Dib32(16, 16, Solid(0xFF, 0, 0))));

        WithDecoded(ico, composite =>
        {
            var set = Assert.IsType<VariantRasterContent>(composite.Content);

            Assert.Equal(["16 x 16", "16 x 16"], set.Variants.Select(variant => variant.Label));
            Assert.Equal(["32-bit BMP", "4-bit BMP"], set.Variants.Select(variant => variant.Detail));
        });
    }

    [Fact]
    public void Decode_EveryVariantSelectsToItsOwnPixels()
    {
        var ico = Ico((16, Dib32(16, 16, Solid(0xFF, 0x00, 0x00))), (32, Dib32(32, 32, Solid(0x00, 0xFF, 0x00))));

        WithDecoded(ico, composite =>
        {
            var set = Assert.IsType<VariantRasterContent>(composite.Content);

            for (var i = 0; i < set.Variants.Count; i++)
            {
                Assert.True(set.Select(i) || set.ActiveIndex == i);

                var raster = Assert.IsType<RasterContent>(set.Active);
                Assert.Equal(set.Variants[i].Width, raster.Image.Width);
            }
        });
    }

    [Fact]
    public void Decode_ReportsWhatTheSetHolds()
    {
        var ico = Ico((16, DibIndexed(16, 16, bits: 4, (_, _) => 1, [(0, 0, 0), (0xFF, 0, 0)])), (32, Dib32(32, 32, Solid(1, 2, 3))), (256, PngBytes(256)));

        WithDecoded(ico, composite =>
        {
            var facts = composite.FormatSpecificSnapshot().ToDictionary(pair => pair.Key, pair => pair.Value);

            Assert.Equal("3", facts["Entries"]);
            Assert.Equal("16 x 16 to 256 x 256", facts["Size Range"]);
            Assert.Equal("4-bit, 32-bit", facts["Depths"]);
            Assert.Equal("BMP x2, PNG", facts["Encodings"]);
        });
    }

    [Fact]
    public void Decode_SkipsAnUnreadableEntryAndKeepsTheRest()
    {
        var ico = Ico((16, Dib32(16, 16, Solid(1, 2, 3))), (32, Dib32(32, 32, Solid(1, 2, 3))));

        // Leave the first entry's header intact and cut its payload off after it: the entry still
        // describes itself, and still cannot produce an image.
        BinaryPrimitives.WriteUInt32LittleEndian(ico.AsSpan(6 + 8), 48);

        WithDecoded(ico, composite =>
        {
            var set = Assert.IsType<VariantRasterContent>(composite.Content);

            Assert.Equal("32 x 32", Assert.Single(set.Variants).Label);
        });
    }

    /// <summary>
    /// A PNG renamed .ico is a favicon that works in every browser, so it turns up in real folders.
    /// Refusing it would be correct about the container and useless to the person looking at it.
    /// </summary>
    [Fact]
    public void Decode_APlainImageWearingTheExtension_IsStillShown()
    {
        WithDecoded(PngBytes(64), composite =>
        {
            var raster = Assert.IsType<RasterContent>(composite.Content);

            Assert.Equal(64, raster.Image.Width);
        });
    }

    private const int MinimumIco = 6 + 16;

    private static byte[] Ico(params (int Size, byte[] Payload)[] entries)
    {
        var directory = 6 + (16 * entries.Length);
        var bytes = new byte[directory + entries.Sum(entry => entry.Payload.Length)];

        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2), 1); // icon, not cursor
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), (ushort)entries.Length);

        var offset = directory;

        for (var i = 0; i < entries.Length; i++)
        {
            var slot = bytes.AsSpan(6 + (i * 16));

            slot[0] = (byte)(entries[i].Size == 256 ? 0 : entries[i].Size);
            slot[1] = slot[0];

            BinaryPrimitives.WriteUInt16LittleEndian(slot[4..], 1); // colour planes
            BinaryPrimitives.WriteUInt32LittleEndian(slot[8..], (uint)entries[i].Payload.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(slot[12..], (uint)offset);

            entries[i].Payload.CopyTo(bytes, offset);
            offset += entries[i].Payload.Length;
        }

        return bytes;
    }

    /// <summary>Writes a depth into one directory entry, the way an icon editor records intent.</summary>
    private static void Declare(byte[] ico, int index, ushort bits)
        => BinaryPrimitives.WriteUInt16LittleEndian(ico.AsSpan(6 + (index * 16) + 6), bits);

    private static Func<int, int, (byte R, byte G, byte B, byte A)> Solid(byte r, byte g, byte b)
        => (_, _) => (r, g, b, 0xFF);

    private static byte[] Dib32(int width, int height, Func<int, int, (byte R, byte G, byte B, byte A)> pixel,
        Func<int, int, bool>? transparent = null)
    {
        var stride = Stride(width, 32);
        var pixels = new byte[stride * height];

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var (r, g, b, a) = pixel(x, y);
            var target = ((height - 1 - y) * stride) + (x * 4);

            pixels[target + 0] = b;
            pixels[target + 1] = g;
            pixels[target + 2] = r;
            pixels[target + 3] = a;
        }

        return Dib(width, height, 32, [], pixels, Mask(width, height, transparent));
    }

    private static byte[] DibIndexed(int width, int height, ushort bits, Func<int, int, int> index, (byte R, byte G, byte B)[] palette, Func<int, int, bool>? transparent = null)
    {
        var stride = Stride(width, bits);
        var pixels = new byte[stride * height];

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var row = (height - 1 - y) * stride;
            var value = index(x, y);

            switch (bits)
            {
                case 8:
                    pixels[row + x] = (byte)value;
                    break;

                case 4:
                    pixels[row + (x / 2)] |= (byte)(value << ((x & 1) == 0 ? 4 : 0));
                    break;

                default:
                    pixels[row + (x / 8)] |= (byte)(value << (7 - (x & 7)));
                    break;
            }
        }

        var table = new byte[palette.Length * 4];
        for (var i = 0; i < palette.Length; i++)
        {
            table[(i * 4) + 0] = palette[i].B;
            table[(i * 4) + 1] = palette[i].G;
            table[(i * 4) + 2] = palette[i].R;
        }

        return Dib(width, height, bits, table, pixels, Mask(width, height, transparent), (uint)palette.Length);
    }

    private static byte[] Dib16(int width, int height, Func<int, int, int> pixel)
    {
        var stride = Stride(width, 16);
        var pixels = new byte[stride * height];

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
            BinaryPrimitives.WriteUInt16LittleEndian(pixels.AsSpan(((height - 1 - y) * stride) + (x * 2)), (ushort)pixel(x, y));

        return Dib(width, height, 16, [], pixels, Mask(width, height, null));
    }

    private static byte[] Mask(int width, int height, Func<int, int, bool>? transparent)
    {
        var stride = Stride(width, 1);
        var mask = new byte[stride * height];

        if (transparent is null)
            return mask;

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            if (transparent(x, y))
                mask[((height - 1 - y) * stride) + (x / 8)] |= (byte)(1 << (7 - (x & 7)));
        }

        return mask;
    }

    private static byte[] Dib(int width, int height, ushort bits, byte[] palette, byte[] pixels, byte[] mask, uint paletteCount = 0)
    {
        var bytes = new byte[40 + palette.Length + pixels.Length + mask.Length];
        var header = bytes.AsSpan();

        BinaryPrimitives.WriteUInt32LittleEndian(header, 40);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], width);
        BinaryPrimitives.WriteInt32LittleEndian(header[8..], height * 2);
        BinaryPrimitives.WriteUInt16LittleEndian(header[12..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(header[14..], bits);
        BinaryPrimitives.WriteUInt32LittleEndian(header[32..], paletteCount);

        palette.CopyTo(bytes, 40);
        pixels.CopyTo(bytes, 40 + palette.Length);
        mask.CopyTo(bytes, 40 + palette.Length + pixels.Length);

        return bytes;
    }

    private static int Stride(int width, int bits) => (((width * bits) + 31) / 32) * 4;

    private static byte[] PngBytes(int size, int detail = 0)
    {
        var info = new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);
        bitmap.Erase(new SKColor(0x40, 0x80, 0xC0, 0xFF));

        var random = new Random(detail);
        for (var y = 0; y < size && detail > 0; y++)
        for (var x = 0; x < size; x++)
            bitmap.SetPixel(x, y, new SKColor((byte)random.Next(detail), (byte)random.Next(detail), (byte)random.Next(detail), 0xFF));

        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);

        return encoded.ToArray();
    }

    /// <summary>Decodes the one entry an icon holds, straight through the reader.</summary>
    private static SKBitmap Decode(byte[] ico)
    {
        var entry = Assert.Single(IcoReader.ReadEntries(ico));
        var decoded = IcoReader.Decode(ico, entry);

        Assert.NotNull(decoded);

        var image = decoded.Value;
        var bitmap = new SKBitmap(new SKImageInfo(image.Width, image.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));

        for (var y = 0; y < image.Height; y++)
        for (var x = 0; x < image.Width; x++)
        {
            var i = ((y * image.Width) + x) * 4;
            bitmap.SetPixel(x, y, new SKColor(image.Pixels[i], image.Pixels[i + 1], image.Pixels[i + 2], image.Pixels[i + 3]));
        }

        return bitmap;
    }

    private static SKColor PixelAt(SKBitmap bitmap, int x, int y) => bitmap.GetPixel(x, y);

    private static void WithDecoded(byte[] ico, Action<Composite> assert)
    {
        var path = Path.Combine(Path.GetTempPath(), $"lyra-ico-{Guid.NewGuid():N}.ico");
        File.WriteAllBytes(path, ico);

        try
        {
            using var composite = new Composite(new FileInfo(path));
            new IcoDecoder().DecodeAsync(composite, CancellationToken.None).GetAwaiter().GetResult();

            assert(composite);
        }
        finally
        {
            File.Delete(path);
        }
    }
}