using Lyra.Imaging.Content;
using Lyra.UI.Components;
using Lyra.UI.Components.Layout;
using Lyra.UI.Components.Primitives;
using Lyra.UI.SupportingTypes;
using Lyra.UI.Theme;

namespace Lyra.Renderer.GUI.Sections;

public sealed class HelpSection : IUISection
{
    private readonly VStack _root;
    private readonly VStack _keys;
    private readonly VStack _descriptions;

    // Rows whose visibility depends on UIState.
    private readonly Label _dirEdgeKey;
    private readonly Label _dirEdgeDesc;
    private readonly Label _samplingKey;
    private readonly Label _samplingDesc;

    public IComponent Root => _root;

    public HelpSection()
    {
        _keys = new VStack
        {
            HorizontalSize = SizeMode.Shrink,
            VerticalSize = SizeMode.Shrink,
            HorizontalAlign = HAlign.Right,
            Spacing = 2
        };

        _descriptions = new VStack
        {
            HorizontalSize = SizeMode.Shrink,
            VerticalSize = SizeMode.Shrink,
            HorizontalAlign = HAlign.Left,
            Spacing = 2
        };

        // Always-visible rows.
        AddRow("← / →",   "Previous / Next image");
        AddRow("⌘← / ⌘→", "First / Last image");

        (_dirEdgeKey, _dirEdgeDesc) = AddRow("⌥← / ⌥→", "Prev / Next directory edge");

        AddRow("↲",             "Reveal in Finder");
        AddRow("+ / −",         "Zoom in / out");
        AddRow("Mouse Wheel",   "Zoom at cursor");
        AddRow("0",             "Fit screen / Original size");
        AddRow("I",             "Toggle info");
        AddRow("U",             "Toggle sidebar");
        AddRow("H",             "Toggle help");
        AddRow("B",             "Toggle background");
        
        (_samplingKey, _samplingDesc) = AddRow("S", "Toggle sampling");

        AddRow("F",   "Toggle fullscreen");
        AddRow("Esc", "Abort / Quit");

        var grid = new HStack
        {
            HorizontalSize = SizeMode.Shrink,
            VerticalSize = SizeMode.Shrink,
            VerticalAlign = VAlign.Top,
            Spacing = 12
        };
        grid.AddComponents(_keys, _descriptions);

        _root = new VStack
        {
            HorizontalSize = SizeMode.Expand,
            VerticalSize = SizeMode.Shrink,
            HorizontalAlign = HAlign.Left,
            Padding = new Padding(8)
        };
        _root.AddComponents(grid);
    }

    public void Refresh(UIState state)
    {
        var multiDir = state.Navigation.DirectoryCount is not null && state.Navigation.DirectoryIndex is not null;
        _dirEdgeKey.Present  = multiDir;
        _dirEdgeDesc.Present = multiDir;

        var vector = state.Composite?.Content?.Kind == CompositeContentKind.Vector;
        _samplingKey.Present  = !vector;
        _samplingDesc.Present = !vector;
    }

    private (Label key, Label desc) AddRow(string key, string description)
    {
        var k = new Label(key)         { Color = Palette.Dim };
        var d = new Label(description) { Color = Palette.Dim };
        _keys.AddComponents(k);
        _descriptions.AddComponents(d);
        return (k, d);
    }
}