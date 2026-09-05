namespace Lyra.Renderer.GUI.Layers;

using UI;
using UI.Components;
using UI.Components.Controls;
using UI.Components.Layout;
using UI.Components.Primitives;
using UI.SupportingTypes;
using SkiaSharp;

// ============================================================================
//  StatusLayer - centered status text ("No image", "Loading...", drop progress)
// ----------------------------------------------------------------------------
//  Renders below the main UI.  Does not block input or visuals.
//  The caller decides what text and color to show via SetStatus, and whether
//  a progress bar sits under it; this class only handles presentation.
//
//  When hidden (text is null), the layer root is not Present,
//  so LyraUI skips layout and rendering entirely.
// ============================================================================
public class StatusLayer : IDisposable
{
    private readonly Label _label;
    private readonly ProgressBar _progress;
    private readonly VStack _root;

    public Layer Layer { get; }

    public StatusLayer(UIContext context)
    {
        _label = new Label("")
            .FontSize(22f)
            .Align(HAlign.Center)
            .Transient()
            .Present(false);

        _progress = new ProgressBar
        {
            HorizontalAlign = HAlign.Center,
            Present = false
        };

        _root = new VStack()
            .Expand()
            .ContentAlign(HAlign.Center)
            .Spacing(10f)
            .Transient()
            .Present(false)
            .Child(_label)
            .Child(_progress);

        Layer = context.AddLayer("Status");
        Layer.Root = _root;
    }

    public void SetStatus(string? text, SKColor textColor)
    {
        if (text is not null)
        {
            _label.Text = text;
            _label.Color = textColor;
        }

        _label.Present = text is not null;
        UpdatePresence();
    }
    
    public void SetProgress(bool visible, float value, bool indeterminate, SKColor color)
    {
        _progress.Present = visible;

        if (visible)
        {
            _progress.Color = color;
            _progress.Indeterminate = indeterminate;
            _progress.Value = value;
        }

        UpdatePresence();
    }

    /// <summary>
    /// The layer participates in layout only while it has something in it. Derived from the two
    /// parts rather than set by either, so whichever is updated last cannot hide the other.
    /// </summary>
    private void UpdatePresence() => _root.Present = _label.Present || _progress.Present;

    public void Dispose()
    {
        // Component tree disposed by Layer via UIContext.Dispose.
    }
}