using System.Buffers.Binary;
using System.Text;
using Lyra.Imaging.Decoding.Decoders;
using Lyra.Imaging.Tests.Support;
using Lyra.ManagedCodecs.Tests.Gif;
using Lyra.ManagedCodecs.Tests.Png;
using Lyra.ManagedCodecs.Tests.Texture;
using SkiaSharp;

namespace Lyra.Imaging.Tests.Decoding.Sweep;

/// <param name="Name">How the sweep reports it.</param>
/// <param name="Extension">The name it is decoded under, which picks the decoder.</param>
/// <param name="Build">A small, valid file of the format, made fresh for each test.</param>
/// <param name="Available">False when what reads it is missing on this machine.</param>
internal sealed record SweepFixture(string Name, string Extension, Func<byte[]> Build, Func<bool>? Available = null)
{
    public bool IsAvailable => Available?.Invoke() ?? true;
}

/// <summary>One small valid file of every format a decoder reads, generated rather than read from disk.</summary>
internal static class SweepFixtures
{
    public static IReadOnlyList<SweepFixture> All { get; } =
    [
        new("png", ".png", () => Encode(SKEncodedImageFormat.Png)),
        new("jpeg", ".jpg", () => Encode(SKEncodedImageFormat.Jpeg)),
        new("webp", ".webp", () => Encode(SKEncodedImageFormat.Webp)),
        new("bmp", ".bmp", () => FromTempFile(MinimalImageBuilder.WriteBmp())),
        new("tga", ".tga", () => FromTempFile(MinimalImageBuilder.WriteTga())),
        new("ico", ".ico", Ico),
        new("icns", ".icns", Icns),
        new("gif", ".gif", () => new GifBuilder(8, 6).AddSolid(1).AddSolid(2).Build()),
        new("apng", ".png", () => new ApngBuilder(8, 6).AddSolid(0xFFFF0000).AddSolid(0xFF0000FF).Build()),
        new("tiff", ".tif", () => TiffBuilder.Build(TiffBuilder.Rgb(8, 6))),
        new("qoi", ".qoi", Qoi),
        new("hdr", ".hdr", Radiance),
        new("dds", ".dds", () => DdsTestFile.Legacy(8, 8, 1, "DXT1", [.. Enumerable.Range(0, 4).SelectMany(_ => DdsTestFile.RedBc1Block())])),
        new("ktx", ".ktx", Ktx1),
        new("psd", ".psd", Psd),
        new("svg", ".svg", () => Encoding.UTF8.GetBytes("""<svg xmlns="http://www.w3.org/2000/svg" width="16" height="12"><rect width="16" height="12" fill="teal"/></svg>""")),
        new("exr", ".exr", () => Convert.FromBase64String(ExrGrayscaleTests.SingleChannelExrBase64)),
        new("jp2", ".jp2", () => Convert.FromBase64String(J2KColorSpaceTests.SyccJp2Base64)),
        new("htj2k", ".jhc", () => Convert.FromBase64String(Htj2kTests.Codestream)),
        new("jxl", ".jxl", () => Convert.FromBase64String(JxlColorSpaceTests.DisplayP3JxlBase64), () => LibJxlReady.Value),
        new("jxl-animated", ".jxl", () => Convert.FromBase64String(JxlAnimationTests.ThreeFrames), () => LibJxlReady.Value),
        new("avif", ".avif", () => Convert.FromBase64String(HeifStillFixtures.AvifDisplayP3), () => Libheif.Ready),
        new("dng", ".dng", () => DngBuilder.Build(new DngBuilder.Spec()), () => LibRawReady.Value),
        new("gltf", ".gltf", Gltf)
    ];

    public static SweepFixture Named(string name) => All.Single(fixture => fixture.Name == name);

    private static readonly Lazy<bool> LibRawReady = new(() => NativeWrapper.TryLoad("libraw_native", typeof(RawDecoder).Assembly));

    private static readonly Lazy<bool> LibJxlReady = new(() => NativeWrapper.TryLoad("libjxl_native", typeof(JxlDecoder).Assembly));

    private static byte[] Encode(SKEncodedImageFormat format, int width = 8, int height = 6)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
            bitmap.SetPixel(x, y, new SKColor((byte)(x * 30), (byte)(y * 40), 128));

        using var data = bitmap.Encode(format, 90);
        return data.ToArray();
    }

    private static byte[] FromTempFile(string path)
    {
        try
        {
            return File.ReadAllBytes(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>One 16-pixel rendition, stored as PNG.</summary>
    private static byte[] Ico()
    {
        var png = Encode(SKEncodedImageFormat.Png, 16, 16);
        var file = new byte[6 + 16 + png.Length];

        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(4), 1);

        file[6] = 16;
        file[7] = 16;
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(10), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(12), 32);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(14), (uint)png.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(18), 22);
        png.CopyTo(file, 22);

        return file;
    }

    /// <summary>One 128-pixel PNG rendition.</summary>
    private static byte[] Icns()
    {
        var png = Encode(SKEncodedImageFormat.Png, 128, 128);
        var file = new byte[8 + 8 + png.Length];

        "icns"u8.CopyTo(file);
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(4), (uint)file.Length);
        "ic07"u8.CopyTo(file.AsSpan(8));
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(12), (uint)(8 + png.Length));
        png.CopyTo(file, 16);

        return file;
    }

    /// <summary>Every pixel as a full RGBA op, then the end marker.</summary>
    private static byte[] Qoi()
    {
        const int width = 4, height = 3;
        using var file = new MemoryStream();
        Span<byte> header = stackalloc byte[14];

        "qoif"u8.CopyTo(header);
        BinaryPrimitives.WriteUInt32BigEndian(header[4..], width);
        BinaryPrimitives.WriteUInt32BigEndian(header[8..], height);
        header[12] = 4;
        header[13] = 0;
        file.Write(header);

        for (var i = 0; i < width * height; i++)
            file.Write([0xFF, (byte)(i * 20), 64, 200, 255]);

        file.Write([0, 0, 0, 0, 0, 0, 0, 1]);
        return file.ToArray();
    }

    /// <summary>Flat RGBE scanlines, which a width under eight always uses.</summary>
    private static byte[] Radiance()
    {
        var header = Encoding.ASCII.GetBytes("#?RADIANCE\nFORMAT=32-bit_rle_rgbe\n\n-Y 4 +X 4\n");
        var pixels = Enumerable.Range(0, 16).SelectMany(i => new byte[] { (byte)(64 + i * 8), 96, 32, 130 });
        return [.. header, .. pixels];
    }

    /// <summary>KTX 1: one level of uncompressed RGBA8.</summary>
    private static byte[] Ktx1()
    {
        const int width = 4, height = 4;
        byte[] identifier = [0xAB, (byte)'K', (byte)'T', (byte)'X', (byte)' ', (byte)'1', (byte)'1', 0xBB, 0x0D, 0x0A, 0x1A, 0x0A];
        uint[] fields = [0x04030201, 0x1401, 1, 0x1908, 0x8058, 0x1908, width, height, 0, 0, 1, 1, 0];

        var file = new byte[identifier.Length + fields.Length * 4 + 4 + width * height * 4];
        identifier.CopyTo(file, 0);

        for (var i = 0; i < fields.Length; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(12 + i * 4), fields[i]);

        var data = 12 + fields.Length * 4;
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(data), width * height * 4);

        for (var i = 0; i < width * height * 4; i++)
            file[data + 4 + i] = (byte)(i * 7);

        return file;
    }

    /// <summary>An 8-bit RGB document with no layers, its composite stored raw.</summary>
    private static byte[] Psd()
    {
        const int width = 6, height = 4, channels = 3;
        var file = new byte[26 + 4 + 4 + 4 + 2 + width * height * channels];

        "8BPS"u8.CopyTo(file);
        BinaryPrimitives.WriteUInt16BigEndian(file.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt16BigEndian(file.AsSpan(12), channels);
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(14), height);
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(18), width);
        BinaryPrimitives.WriteUInt16BigEndian(file.AsSpan(22), 8);
        BinaryPrimitives.WriteUInt16BigEndian(file.AsSpan(24), 3);

        // Colour mode data, image resources and layers are all empty; compression 0 is raw.
        for (var i = 40; i < file.Length; i++)
            file[i] = (byte)(i * 11);

        return file;
    }

    /// <summary>A model whose only content is one embedded texture.</summary>
    private static byte[] Gltf()
    {
        var png = Convert.ToBase64String(Encode(SKEncodedImageFormat.Png, 4, 4));

        return Encoding.UTF8.GetBytes($$"""
            {
              "asset": { "version": "2.0" },
              "images": [ { "name": "albedo", "uri": "data:image/png;base64,{{png}}" } ]
            }
            """);
    }
}