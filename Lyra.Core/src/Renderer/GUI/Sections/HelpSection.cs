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
        _keys = new VStack()
            .Align(HAlign.Right)
            .Spacing(2);

        _descriptions = new VStack()
            .Align(HAlign.Left)
            .Spacing(2);

        var isMac = OperatingSystem.IsMacOS();
        
        AddRow("← / →", "Previous / Next image");
        AddRow(isMac ? "⌘← / ⌘→" : "Home / End", "First / Last image");

        (_dirEdgeKey, _dirEdgeDesc) = AddRow(isMac ? "⌥← / ⌥→" : "Ctrl← / Ctrl→", "Prev / Next directory edge");

        AddRow("↲",             isMac ? "Reveal in Finder" : "Reveal in file explorer");
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

        _root = new VStack()
            .ExpandH()
            .Align(HAlign.Left)
            .Padding(8)
            .Child(new HStack()
                .Align(VAlign.Top)
                .Spacing(12)
                .Children(_keys, _descriptions));
    }

    public void Refresh(UIState state)
    {
        // The edge-modifier row doubles as group navigation in duplicates mode.
        if (state.AppStates.InDuplicatesMode)
        {
            _dirEdgeKey.Present  = true;
            _dirEdgeDesc.Present = true;
            _dirEdgeDesc.Text    = "Prev / Next group";
        }
        else
        {
            var multiDir = state.Navigation.DirectoryCount is not null && state.Navigation.DirectoryIndex is not null;
            _dirEdgeKey.Present  = multiDir;
            _dirEdgeDesc.Present = multiDir;
            _dirEdgeDesc.Text    = "Prev / Next directory edge";
        }

        // No sampling row for content that has no sampling to choose.
        var resolutionIndependent = state.Composite?.Content?.IsResolutionIndependent == true;
        _samplingKey.Present  = !resolutionIndependent;
        _samplingDesc.Present = !resolutionIndependent;
    }

    private (Label key, Label desc) AddRow(string key, string description)
    {
        var k = new Label(key).Color(Palette.Dim);
        var d = new Label(description).Color(Palette.Dim);
        _keys.AddComponent(k);
        _descriptions.AddComponent(d);
        return (k, d);
    }
}