using Lyra.Common;
using Lyra.Common.Events;
using Lyra.Common.SystemExtensions;
using Lyra.Imaging.Content;
using Lyra.Imaging.Psd;
using Lyra.Imaging.Psd.Core.SectionData;
using static System.Threading.Thread;
using static Lyra.Common.Events.EventManager;

namespace Lyra.Imaging.Codecs;

public class PsdDecoder : IImageDecoder, IDrawableSizeAware
{
    public bool CanDecode(ImageFormatType format) => format is ImageFormatType.Psd or ImageFormatType.Psb;

    private int _drawableWidth = 3840;
    private int _drawableHeight = 2160;

    public PsdDecoder()
    {
        Subscribe<DrawableSizeChangedEvent>(OnDrawableSizeChanged);
    }

    public async Task DecodeAsync(Composite composite, CancellationToken ct)
    {
        var path = composite.FileInfo.FullName;
        composite.DecoderName = GetType().Name;
        Logger.Debug($"[PsdDecoder] [Thread: {CurrentThread.GetNameOrId()}] Decoding: {path}");

        ct.ThrowIfCancellationRequested();

        await using var file = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            options: FileOptions.RandomAccess);

        try
        {
            var header = PsdDocument.ReadHeader(file);
            ProcessHeader(header, composite);

            file.Position = 0; // Important!
        }
        catch (Exception e)
        {
            Logger.Warning($"[PsdDecoder] Header could not be read: {path}\n{e.Message}");
            return;
        }

        try
        {
            var (image, metadata) = await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();

                var psd = PsdDocument.ReadDocument(file);
                var decoded = psd.DecodePreview(file, _drawableWidth, _drawableHeight, null, ct);

                return (decoded, psd.PsdMetadata);
            }, ct);

            ProcessMetadata(metadata, composite);
            composite.Content = new RasterContent(image);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            Logger.Warning($"[PsdDecoder] Image could not be loaded: {path} \n{e.Message}");
        }
    }

    private void ProcessHeader(FileHeader header, Composite composite)
    {
        composite.FullWidth = header.Width;
        composite.FullHeight = header.Height;
        composite.FormatSpecific.Add("Color Mode", $"{header.ColorMode}");
        composite.FormatSpecific.Add("Channels", $"{header.NumberOfChannels}");
        composite.FormatSpecific.Add("Depth per Channel", $"{header.Depth}-bit");
    }

    private void ProcessMetadata(PsdMetadata metadata, Composite composite)
    {
        composite.FormatSpecific.Add("Compression", $"{metadata.CompressionType ?? "none"}");
        composite.FormatSpecific.Add("Embedded ICC Profile", $"{metadata.EmbeddedIccProfileName ?? "none"}");

        if (metadata.EffectiveIccProfileName is not "Embedded ICC Profile")
            composite.FormatSpecific.Add("Effective ICC Profile", $"{metadata.EffectiveIccProfileName ?? "none"}");
    }

    public void OnDrawableSizeChanged(DrawableSizeChangedEvent e)
    {
        _drawableWidth = e.Width;
        _drawableHeight = e.Height;
    }
}