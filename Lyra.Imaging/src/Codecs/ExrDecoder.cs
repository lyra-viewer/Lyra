using Lyra.Common;
using Lyra.Imaging.Interop;

namespace Lyra.Imaging.Codecs;

internal class ExrDecoder : FloatRgbaDecoderBase
{
    public override bool CanDecode(ImageFormatType format) => format == ImageFormatType.Exr;

    protected override bool LoadPixels(string path, out IntPtr ptr, out int width, out int height)
    {
        var result = ExrNative.load_exr_rgba(path, out ptr, out width, out height);
        if (!result)
        {
            var errorPtr = ExrNative.get_last_exr_error();
            var error = NativeErrors.GetUtf8ZOrAnsiZ(errorPtr);
            Logger.Error($"[ExrDecoder] Native error: {error}");
        }

        return result;
    }

    protected override void FreePixels(IntPtr ptr) => ExrNative.free_exr_pixels(ptr);
}