using Lyra.Common;
using Lyra.Imaging.Interop;

namespace Lyra.Imaging.Codecs;

internal class HdrDecoder : FloatRgbaDecoderBase
{
    public override bool CanDecode(ImageFormatType format) => format == ImageFormatType.Hdr;

    protected override bool LoadPixels(string path, out IntPtr ptr, out int width, out int height)
    {
        var result = HdrNative.load_hdr_rgba(path, out ptr, out width, out height);
        if (!result)
        {
            var errorPtr = HdrNative.get_last_hdr_error();
            var error = NativeErrors.GetUtf8ZOrAnsiZ(errorPtr);
            Logger.Error($"[HdrDecoder] Native error: {error}");
        }

        return result;
    }

    protected override void FreePixels(IntPtr ptr) => HdrNative.free_hdr_pixels(ptr);
}