namespace Lyra.Imaging.Content.Variants;

/// <summary>
/// Leases: decoded renditions lent out for others to be made from, as a contact sheet is from its
/// textures. Lent content is never disposed until it comes back, whatever happens meanwhile.
/// </summary>
public sealed partial class VariantContainer
{
    /// <summary>How many leases each lent content has out. Lent content is never disposed until returned.</summary>
    private readonly Dictionary<ICompositeContent, int> _leases = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Lends a decoded rendition, for another to be made from it without decoding it again. The
    /// content stays alive until the lease is disposed, even if it is trimmed meanwhile. Null when
    /// the rendition is not decoded.
    /// </summary>
    public ContentLease? Borrow(int index)
    {
        lock (_gate)
        {
            if (_disposed || index < 0 || index >= _contents.Count || _contents[index] is not { } content)
                return null;

            _leases[content] = _leases.GetValueOrDefault(content) + 1;
            return new ContentLease(this, content);
        }
    }

    internal void Return(ICompositeContent content)
    {
        var dispose = false;

        lock (_gate)
        {
            if (!_leases.TryGetValue(content, out var count))
                return;

            if (count > 1)
            {
                _leases[content] = count - 1;
                return;
            }

            _leases.Remove(content);

            if (_disposed && _retired.Remove(content))
                dispose = true;
        }

        if (dispose)
            content.Dispose();
    }
}