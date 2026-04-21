using Lyra.UI.Components;

namespace Lyra.UI;

/// <summary>
/// A named z-ordered slice of the UI.
///
/// Layers are stored in bottom-to-top order within UIContext.
/// Rendering walks the list forward (bottom layer first).
/// Input dispatch walks the list backward (top layer first).
///
/// <see cref="BlocksInput"/> - when true, layers below this one
///   receive no pointer or scroll events, even if nothing in this
///   layer was hit.  Use for context menus and modal panels.
///
/// <see cref="BlocksVisual"/> - when true, a scrim is rendered
///   before this layer's content.  Use for modal overlays that
///   dim the background.  (Scrim rendering is the caller's
///   responsibility; this flag is advisory.)
/// </summary>
public class Layer(string name) : IDisposable
{
    public string Name { get; } = name;
    public IComponent? Root { get; set; }
    public bool BlocksInput { get; set; }
    public bool BlocksVisual { get; set; }

    public void Dispose() => Root?.Dispose();
}