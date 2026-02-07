using Lyra.Common;
using Lyra.Common.SystemExtensions;
using Lyra.Imaging.Content;
using Svg.Skia;
using static System.Threading.Thread;

namespace Lyra.Imaging.Codecs;

public class SvgDecoder : IImageDecoder
{
    public bool CanDecode(ImageFormatType format) => format is ImageFormatType.Svg;

    public Task DecodeAsync(Composite composite, CancellationToken ct)
    {
        var path = composite.FileInfo.FullName;
        composite.DecoderName = GetType().Name;
        Logger.Debug($"[SvgDecoder] [Thread: {CurrentThread.GetNameOrId()}] Decoding: {path}");

        try
        {
            ct.ThrowIfCancellationRequested();

            var svg = new SKSvg();
            svg.Load(path);

            ct.ThrowIfCancellationRequested();

            var picture = svg.Picture;
            if (picture == null)
            {
                Logger.Warning($"[SvgDecoder] SVG picture is null: {path}");
                return Task.CompletedTask;
            }

            var originalBounds = picture.CullRect;
            if (originalBounds.IsEmpty || originalBounds.Width < 1 || originalBounds.Height < 1)
                Logger.Debug($"[SvgDecoder] Detected empty or invalid CullRect: {path}");

            composite.Content = new VectorContent(picture);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Warning($"[SvgDecoder] Failed to load {path}: {ex.Message}");
            throw;
        }

        return Task.CompletedTask;
    }
}