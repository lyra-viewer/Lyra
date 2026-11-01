using Lyra.Common;
using Lyra.Imaging.Decoding.Support;
using Lyra.Imaging.Interop;

namespace Lyra.Imaging.Decoding.Decoders;

internal class ExrDecoder : FloatRgbaDecoderBase
{
    public override IReadOnlyList<ImageFormatType> Formats { get; } = [ImageFormatType.Exr];

    protected override FloatImageBuffer LoadPixels(DecodeContext context, CancellationToken ct)
    {
        var path = context.Source.RequireFile();

        IntPtr ptr;
        int width, height;
        ExrNative.ExrInfo info;

        if (ExrNative.MemoryLoadAvailable && NativeFileBuffer.ShouldBuffer(context.Source.Length ?? 0))
        {
            using var data = NativeFileBuffer.Read(path, ct, out var readMs, context.ReportTransferred);
            context.CompleteTransfer((long)data.Length, readMs);
            ct.ThrowIfCancellationRequested();

            if (ExrNative.LoadFromMemory(data.Data, data.Length, out ptr, out width, out height, out info) && ptr != IntPtr.Zero)
            {
                Describe(context, info);
                return FloatImageBuffer.FromNative(ptr, width, height, ExrNative.free_exr_pixels);
            }

            if (ExrNative.MemoryLoadAvailable)
                throw NativeErrors.DecodeFailed(ExrNative.get_last_exr_error(), path);

            Logger.Warning(typeof(ExrDecoder), "Native library has no memory entry point; falling back to path decode.");
        }

        if (!ExrNative.load_exr_rgba(path, out ptr, out width, out height, out info) || ptr == IntPtr.Zero)
            throw NativeErrors.DecodeFailed(ExrNative.get_last_exr_error(), path);

        Describe(context, info);

        return FloatImageBuffer.FromNative(ptr, width, height, ExrNative.free_exr_pixels);
    }

    private static void Describe(DecodeContext context, ExrNative.ExrInfo info)
    {
        if (info.BitsPerChannel > 0)
            context.AddFormatSpecific("Bit Depth", $"{info.BitsPerChannel}-bit {(info.IsFloat != 0 ? "float" : "integer")}");

        context.AddFormatSpecific("Has Alpha", info.HasAlpha != 0);
        context.AddFormatSpecific("Color Space", info.IsGray != 0
            ? "Linear Gray"
            : info.CustomPrimaries != 0
                ? "Linear (custom primaries)"
                : "Linear Rec.709");
    }
}