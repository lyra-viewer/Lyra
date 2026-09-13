using Lyra.Common;
using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Support;
using Lyra.Imaging.Interop;
using Lyra.ManagedCodecs.Raster.Icns;
using SkiaSharp;

namespace Lyra.Imaging.Decoding.Decoders;

/// <summary>
/// Apple icon containers. An entry is either one of Apple's own packings - ARGB planes or RLE24 -
/// or a whole image embedded in the payload, which over the format's life has meant PNG, JPEG and
/// JPEG 2000.
/// </summary>
internal sealed class IcnsDecoder : IconContainerDecoder<IcnsEntry>
{
    public override bool CanDecode(ImageFormatType format) => format == ImageFormatType.Icns;

    protected override string EntryNoun => "icon";

    protected override string CountLabel => "Icons";

    protected override IReadOnlyList<IcnsEntry> ReadEntries(byte[] data) => IcnsReader.ReadEntries(data);

    protected override SKBitmap? DecodeEntry(byte[] data, IcnsEntry entry)
    {
        if (entry.Kind == IcnsPayloadKind.Embedded)
            return DecodeEmbedded(data, entry);

        var decoded = IcnsReader.Decode(data, entry);
        return decoded is { } image ? FromDecodedImage(image) : null;
    }

    protected override string EncodingName(byte[] data, IcnsEntry entry)
    {
        if (entry.Kind != IcnsPayloadKind.Embedded)
            return entry.Kind == IcnsPayloadKind.Argb ? "ARGB" : "RLE24";

        var payload = entry.Payload(data);

        if (IsJpeg2000(payload))
            return "JPEG 2000";

        if (payload.Length >= 4 && payload[0] == 0x89 && payload[1] == (byte)'P' && payload[2] == (byte)'N' && payload[3] == (byte)'G')
            return "PNG";

        if (payload.Length >= 3 && payload[0] == 0xFF && payload[1] == 0xD8 && payload[2] == 0xFF)
            return "JPEG";

        return "Unknown";
    }

    protected override ImageVariant BuildVariant(IcnsEntry entry, SKBitmap bitmap, string encoding)
    {
        var label = $"{bitmap.Width} x {bitmap.Height}";
        if (entry.Scale > 1)
            label += $" @{entry.Scale}x";

        return new ImageVariant(label, bitmap.Width, bitmap.Height, $"{entry.Type.Code} - {encoding}", entry.PayloadLength);
    }

    protected override string Describe(IcnsEntry entry) => $"'{entry.Type.Code}'";

    /// <summary>
    /// Largest first. Two entries can decode to the same size and differ only in whether they are
    /// meant for a retina display, so scale breaks the tie before the type code does.
    /// </summary>
    protected override IEnumerable<DecodedIcon> InPresentationOrder(IEnumerable<DecodedIcon> icons) =>
        icons
            .OrderByDescending(icon => (long)icon.Variant.Width * icon.Variant.Height)
            .ThenByDescending(icon => icon.Entry.Scale)
            .ThenBy(icon => icon.Entry.Type.Code, StringComparer.Ordinal);

    protected override void ReportFormatSpecific(Composite composite, IReadOnlyList<DecodedIcon> icons)
    {
        var retina = icons.Count(icon => icon.Entry.Scale > 1);
        if (retina > 0)
            composite.AddFormatSpecific("Retina Entries", $"{retina} of {icons.Count}");

        AddEncodings(composite, icons);

        composite.AddFormatSpecific("Types", string.Join(", ", icons.Select(icon => icon.Entry.Type.Code).Order(StringComparer.Ordinal)));
    }

    private static SKBitmap? DecodeEmbedded(byte[] data, IcnsEntry entry)
    {
        var payload = entry.Payload(data);

        if (IsJpeg2000(payload))
            return DecodeJpeg2000(data, entry);

        using var skData = SKData.CreateCopy(payload.ToArray());
        return SKBitmap.Decode(skData);
    }

    private static bool IsJpeg2000(ReadOnlySpan<byte> payload) =>
        (payload.Length >= 6 && payload[0] == 0x00 && payload[1] == 0x00 && payload[2] == 0x00 && payload[3] == 0x0C && payload[4] == (byte)'j' && payload[5] == (byte)'P')
        || (payload.Length >= 4 && payload[0] == 0xFF && payload[1] == 0x4F && payload[2] == 0xFF && payload[3] == 0x51);

    private static unsafe SKBitmap? DecodeJpeg2000(byte[] data, IcnsEntry entry)
    {
        var nativePixels = IntPtr.Zero;

        try
        {
            fixed (byte* payload = &data[entry.PayloadOffset])
            {
                var ok = J2KNative.decode_j2k_rgba8_from_memory(
                    (IntPtr)payload,
                    (nuint)entry.PayloadLength,
                    0,
                    out nativePixels,
                    out var width,
                    out var height,
                    out var strideBytes,
                    out _,
                    out _
                );

                if (!ok || nativePixels == IntPtr.Zero)
                {
                    var error = NativeErrors.GetUtf8ZOrAnsiZ(J2KNative.get_last_j2k_error());
                    Logger.Warning($"[IcnsDecoder] JPEG 2000 entry '{entry.Type.Code}' failed: {error}");
                    return null;
                }

                DecoderValidation.RequireSaneDimensions("IcnsDecoder", width, height);
                DecoderValidation.RequireValidStride("IcnsDecoder", strideBytes, width);

                var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
                var bitmap = new SKBitmap(info);

                var src = (byte*)nativePixels;
                var dst = (byte*)bitmap.GetPixels();
                var dstStride = bitmap.Info.RowBytes;
                var rowBytes = Math.Min(strideBytes, dstStride);

                for (var y = 0; y < height; y++)
                    Buffer.MemoryCopy(src + ((nint)y * strideBytes), dst + ((nint)y * dstStride), dstStride, rowBytes);

                return bitmap;
            }
        }
        finally
        {
            if (nativePixels != IntPtr.Zero)
                J2KNative.free_j2k_pixels(nativePixels);
        }
    }
}
