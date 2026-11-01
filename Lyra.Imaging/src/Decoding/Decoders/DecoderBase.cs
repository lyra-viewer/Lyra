using Lyra.Common;
using Lyra.Common.SystemExtensions;
using Lyra.Imaging.Content;
using static System.Threading.Thread;

namespace Lyra.Imaging.Decoding.Decoders;

internal abstract class DecoderBase : IImageDecoder
{
    public abstract IReadOnlyList<ImageFormatType> Formats { get; }

    public virtual DecoderCapabilities Capabilities => DecoderCapabilities.None;

    protected abstract void Decode(DecodeContext context, CancellationToken ct);

    protected string Name => GetType().Name;

    internal void TakeOver(DecodeContext context, CancellationToken ct)
    {
        context.DecoderName = Name;
        Decode(context, ct);
    }

    public Task DecodeAsync(Composite composite, CancellationToken ct)
    {
        var context = DecodeContext.For(composite);
        context.DecoderName = Name;

        Logger.Debug($"[{Name}] [Thread: {CurrentThread.GetNameOrId()}] Decoding: {context.Source.Label}");

        ct.ThrowIfCancellationRequested();

        Decode(context, ct);

        return Task.CompletedTask;
    }
}
