namespace Lyra.Renderer.GUI.Layers;

using UI;
using UI.Components.Layout;
using UI.Components.Primitives;
using UI.SupportingTypes;
using SkiaSharp;

// ============================================================================
//  StatusLayer - centered status text ("No image", "Loading...", drop progress)
// ----------------------------------------------------------------------------
//  Renders below the main UI.  Does not block input or visuals.
//  The caller decides what text and color to show via SetStatus;
//  this class only handles presentation.
//
//  When hidden (text is null), the layer root is not Present,
//  so LyraUI skips layout and rendering entirely.
// ============================================================================
public class StatusLayer : IDisposable
{
    private readonly Label _label;
    private readonly VStack _root;

    public Layer Layer { get; }

    public StatusLayer(UIContext context)
    {
        _label = new Label("")
        {
            FontSize = 22f,
            HorizontalAlign = HAlign.Center,
            Transient = true
        };

        _root = new VStack
        {
            HorizontalSize = SizeMode.Expand,
            VerticalSize = SizeMode.Expand,
            ContentAlign = HAlign.Center,
            Transient = true,
            Present = false
        };
        _root.AddComponents(_label);

        Layer = context.AddLayer("Status");
        Layer.Root = _root;
    }

    /// <summary>
    /// Sets the centered status text. Pass null to hide the overlay.
    /// </summary>
    public void SetStatus(string? text, SKColor textColor)
    {
        if (text is null)
        {
            if (!_root.Present)
                return;

            _root.Present = false;
            return;
        }

        _label.Text = text;
        _label.Color = textColor;
        _root.Present = true;
    }

    public void Dispose()
    {
        // Component tree disposed by Layer via UIContext.Dispose.
    }
}