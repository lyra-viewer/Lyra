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
/// There is deliberately no BlocksVisual flag. Dimming is the job of
/// the layer's own root: ModalOverlay draws its scrim in RenderContent.
/// A layer-level flag would have to be honored by LyraUI.Process, and
/// the version that existed here was set by ShowModal but read by
/// nothing - it looked like it dimmed the background and did not.
/// </summary>
public class Layer(string name, UIContext? context = null) : IDisposable
{
    private IComponent? _root;

    public string Name { get; } = name;
    
    public IComponent? Root
    {
        get => _root;
        set
        {
            if (ReferenceEquals(_root, value))
                return;

            if (_root is not null)
                _root.Context = null;

            _root = value;

            if (_root is not null)
                _root.Context = context;
        }
    }

    public bool BlocksInput { get; set; }

    public void Dispose() => Root?.Dispose();
}