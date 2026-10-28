namespace Lyra.Imaging.Content.Variants;

/// <summary>Decoded content lent by a <see cref="VariantContainer"/>, kept alive until this is disposed.</summary>
public sealed class ContentLease : IDisposable
{
    private readonly VariantContainer _owner;
    private int _returned;

    internal ContentLease(VariantContainer owner, ICompositeContent content)
    {
        _owner = owner;
        Content = content;
    }

    public ICompositeContent Content { get; }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _returned, 1) == 0)
            _owner.Return(Content);
    }
}