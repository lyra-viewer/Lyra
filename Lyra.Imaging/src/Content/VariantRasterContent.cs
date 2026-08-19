namespace Lyra.Imaging.Content;

public sealed class VariantRasterContent : ICompositeContent
{
    private readonly List<ICompositeContent> _contents;

    public VariantRasterContent(IReadOnlyList<ImageVariant> variants, List<ICompositeContent> contents, int active)
    {
        ArgumentNullException.ThrowIfNull(variants);
        ArgumentNullException.ThrowIfNull(contents);

        if (variants.Count == 0 || variants.Count != contents.Count)
            throw new ArgumentException("Variant list and decoded content list must be non-empty and the same length.");

        Variants = variants;
        _contents = contents;
        ActiveIndex = Math.Clamp(active, 0, variants.Count - 1);
    }

    public IReadOnlyList<ImageVariant> Variants { get; }

    public int ActiveIndex { get; private set; }

    /// <summary>The rendition currently being shown. Never null.</summary>
    public ICompositeContent Active => _contents[ActiveIndex];

    // Reported from the active rendition, so the composite's logical size, the drawer and the
    // zoom maths all follow the selection without anyone having to be told about it.
    public bool IsResolutionIndependent => Active.IsResolutionIndependent;
    public float? DecodedWidth => Active.DecodedWidth;
    public float? DecodedHeight => Active.DecodedHeight;

    public long ByteSize => _contents.Sum(c => c.ByteSize);

    /// <summary>
    /// Shows the rendition at <paramref name="index"/>. Returns false when the index is out of
    /// range or already active, so callers can skip redundant relayout.
    /// </summary>
    public bool Select(int index)
    {
        if (index < 0 || index >= _contents.Count || index == ActiveIndex)
            return false;

        ActiveIndex = index;
        return true;
    }

    public void Dispose()
    {
        foreach (var content in _contents)
            content.Dispose();

        _contents.Clear();
    }
}