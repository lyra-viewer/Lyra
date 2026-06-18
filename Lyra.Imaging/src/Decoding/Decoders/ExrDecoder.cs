using Lyra.Common;
using Lyra.Imaging.Decoding.Support;
using Lyra.Imaging.Interop;

namespace Lyra.Imaging.Decoding.Decoders;

internal class ExrDecoder : FloatRgbaDecoderBase
{
    public override bool CanDecode(ImageFormatType format) => format == ImageFormatType.Exr;

    protected override FloatImageBuffer LoadPixels(string path)
    {
        if (!ExrNative.load_exr_rgba(path, out var ptr, out var width, out var height) || ptr == IntPtr.Zero)
        {
            var error = NativeErrors.GetUtf8ZOrAnsiZ(ExrNative.get_last_exr_error());
            throw new InvalidOperationException($"[ExrDecoder] Native error: {error}");
        }

        return FloatImageBuffer.FromNative(ptr, width, height, ExrNative.free_exr_pixels);
    }
}