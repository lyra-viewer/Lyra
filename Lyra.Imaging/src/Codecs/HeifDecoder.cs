using LibHeifSharp;
using Lyra.Common;
using Lyra.Common.SystemExtensions;
using Lyra.Imaging.Content;
using Lyra.Imaging.Pipeline;
using SkiaSharp;
using static System.Threading.Thread;

namespace Lyra.Imaging.Codecs;

internal class HeifDecoder : IImageDecoder
{
    public bool CanDecode(ImageFormatType format) => format == ImageFormatType.Heif;

    public async Task DecodeAsync(Composite composite, CancellationToken ct)
    {
        var path = composite.FileInfo.FullName;
        composite.DecoderName = GetType().Name;
        Logger.Debug($"[HeifDecoder] [Thread: {CurrentThread.GetNameOrId()}] Decoding: {path}");

        await Task.Run(() =>
        {
            try
            {
                using var heifContext = new HeifContext(path);
                using var imageHandle = heifContext.GetPrimaryImageHandle();
                using var decodedImage = imageHandle.Decode(HeifColorspace.Rgb, HeifChroma.InterleavedRgba32);

                var exifData = imageHandle.GetExifMetadata();
                if (exifData != null)
                {
                    using var stream = new MemoryStream(exifData);
                    composite.ExifInfo = MetadataProcessor.ParseMetadata(stream, path);
                }

                var width = decodedImage.Width;
                var height = decodedImage.Height;

                var planeData = decodedImage.GetPlane(HeifChannel.Interleaved);
                var scan0 = planeData.Scan0;

                var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
                var skiaBitmap = new SKBitmap(info);

                unsafe
                {
                    fixed (void* dstPtr = &skiaBitmap.GetPixelSpan().GetPinnableReference())
                    {
                        Buffer.MemoryCopy((void*)scan0, dstPtr, skiaBitmap.ByteCount, skiaBitmap.ByteCount);
                    }
                }

                composite.Content = new RasterContent(SKImage.FromBitmap(skiaBitmap));
                return composite;
            }
            catch (Exception e)
            {
                Logger.Warning($"[HeifDecoder] Image could not be loaded: {path}\n{e}");
                return composite;
            }
        });
    }
}