using Lyra.Common;
using Lyra.Common.SystemExtensions;
using Lyra.Imaging.Content;
using Svg.Skia;
using static System.Threading.Thread;

namespace Lyra.Imaging.Codecs;

public class SvgDecoder : IImageDecoder
{
    public bool CanDecode(ImageFormatType format) => format is ImageFormatType.Svg;

    public async Task DecodeAsync(Composite composite, CancellationToken ct)
    {
        var path = composite.FileInfo.FullName;
        composite.DecoderName = GetType().Name;
        Logger.Debug($"[SkiaDecoder] [Thread: {CurrentThread.GetNameOrId()}] Decoding: {path}");

        await Task.Run(() =>
        {
            var svg = new SKSvg();
            svg.Load(path);

            var picture = svg.Picture;

            if (picture == null)
            {
                Logger.Warning("[SvgDecoder] SVG picture is null.");
                return composite;
            }

            var originalBounds = picture.CullRect;
            if (originalBounds.IsEmpty || originalBounds.Width < 1 || originalBounds.Height < 1) 
                Logger.Debug("[SvgDecoder] Detected empty or invalid CullRect.");

            composite.Content = new VectorContent(picture);
            
            return composite;
        });
    }
}