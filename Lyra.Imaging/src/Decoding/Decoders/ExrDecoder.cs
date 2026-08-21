using Lyra.Common;
using Lyra.Imaging.Content;
using Lyra.Imaging.Decoding.Support;
using Lyra.Imaging.Interop;

namespace Lyra.Imaging.Decoding.Decoders;

internal class ExrDecoder : FloatRgbaDecoderBase
{
    public override bool CanDecode(ImageFormatType format) => format == ImageFormatType.Exr;

    protected override FloatImageBuffer LoadPixels(Composite composite)
    {
        var path = composite.FileInfo.FullName;

        if (!ExrNative.load_exr_rgba(path, out var ptr, out var width, out var height, out var info) || ptr == IntPtr.Zero)
        {
            var error = NativeErrors.GetUtf8ZOrAnsiZ(ExrNative.get_last_exr_error());
            throw new InvalidOperationException($"[ExrDecoder] Native error: {error}");
        }

        Describe(composite, info);

        return FloatImageBuffer.FromNative(ptr, width, height, ExrNative.free_exr_pixels);
    }

    private static void Describe(Composite composite, ExrNative.ExrInfo info)
    {
        if (info.BitsPerChannel > 0)
            composite.AddFormatSpecific("Bit Depth", $"{info.BitsPerChannel}-bit {(info.IsFloat != 0 ? "float" : "integer")}");

        composite.AddFormatSpecific("Alpha", (info.HasAlpha != 0).ToString());
        composite.AddFormatSpecific("Color Space",
            info.IsGray != 0
                ? "Linear Gray"
                : info.CustomPrimaries != 0
                    ? "Linear (custom primaries)"
                    : "Linear Rec.709");
    }
}