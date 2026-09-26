using Lyra.Common;
using Lyra.Common.SystemExtensions;
using Lyra.Imaging.Content;
using static System.Threading.Thread;

namespace Lyra.Imaging.Decoding.Decoders;

internal abstract class DecoderBase : IImageDecoder
{
    public abstract bool CanDecode(ImageFormatType format);

    protected abstract void Decode(Composite composite, string path, CancellationToken ct);

    protected string Name => GetType().Name;

    public Task DecodeAsync(Composite composite, CancellationToken ct)
    {
        var path = composite.FileInfo.FullName;

        composite.DecoderName = Name;
        Logger.Debug($"[{Name}] [Thread: {CurrentThread.GetNameOrId()}] Decoding: {path}");

        ct.ThrowIfCancellationRequested();

        Decode(composite, path, ct);

        return Task.CompletedTask;
    }
}
