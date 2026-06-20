using Lyra.Common;
using Lyra.Imaging.Content;
using Lyra.UI.Components;
using Lyra.UI.Components.Controls;
using Lyra.UI.Components.Layout;
using Lyra.UI.Components.Primitives;
using Lyra.UI.SupportingTypes;
using Lyra.UI.Theme;

namespace Lyra.Renderer.GUI.Sections;

// ============================================================================
//  StructureSection - sidebar collapsible showing the file's internal structure.
// ----------------------------------------------------------------------------
//  Renders Composite.Structure as a stack of per-part collapsibles: each part
//  has a rich header (name + description + size badge) and expands to a list of
//  key-value fields. Currently populated for DDS; any decoder that fills
//  Composite.Structure participates automatically.
// ============================================================================
public sealed class StructureSection : IUISection
{
    private const float KeyColumnWidth = 130f;

    private readonly Collapsible _collapsible;
    private readonly VScrollContainer _groups;

    private Composite? _lastComposite;
    private CompositeState _lastState;

    public Collapsible Collapsible => _collapsible;

    public IComponent Root => _collapsible;

    public StructureSection()
    {
        _groups = new VScrollContainer
        {
            HorizontalSize = SizeMode.Expand,
            VerticalSize = SizeMode.Flexible,
            Spacing = 4,
            Padding = new Padding(0, 4, 0, 0)
        };

        _collapsible = new Collapsible("STRUCTURE")
        {
            HorizontalSize = SizeMode.Expand,
            Present = false
        };
        _collapsible.AddComponent(_groups);
    }

    public void Refresh(UIState state)
    {
        var composite = state.Composite;

        if (composite == _lastComposite && state.CompositeState == _lastState)
            return;

        _lastComposite = composite;
        _lastState = state.CompositeState;

        ApplyData(composite);
    }

    private void ApplyData(Composite? composite)
    {
        _groups.Clear();

        var structure = composite?.Structure;
        if (structure is null || structure.Count == 0)
        {
            _collapsible.Present = false;
            return;
        }

        _collapsible.Present = true;

        foreach (var group in structure)
            _groups.AddComponent(BuildGroupPanel(group));
    }

    private static Collapsible BuildGroupPanel(StructureGroup group)
    {
        var titleColumn = new VStack { HorizontalSize = SizeMode.Expand };
        titleColumn.AddComponent(new Label(group.Name) { Color = Palette.Foreground, Bold = true });

        if (!string.IsNullOrEmpty(group.Description))
            titleColumn.AddComponent(new Label(group.Description) { Color = Palette.Dim });

        var header = new HStack
        {
            Spacing = 8,
            HorizontalSize = SizeMode.Expand,
            VerticalAlign = VAlign.Center,
            Padding = new Padding(0, 2, 0, 2)
        };
        header.AddComponent(titleColumn);

        if (group.SizeBytes.HasValue)
            header.AddComponent(new Label(Formatters.SizeToStr(group.SizeBytes.Value)) { Color = Palette.Muted });

        var panel = new Collapsible(header)
        {
            HorizontalSize = SizeMode.Expand,
            Padding = new Padding(10, 0, 0, 0)
        };

        foreach (var field in group.Fields)
            panel.AddComponent(RenderRow(field));

        return panel;
    }

    private static HStack RenderRow(KeyValuePair<string, string> field)
    {
        var row = new HStack
        {
            Spacing = 8,
            HorizontalSize = SizeMode.Expand,
            Padding = new Padding(16, 1, 0, 1)
        };

        row.AddComponents(
            new Label(field.Key)
            {
                Color = Palette.Dim,
                HorizontalSize = SizeMode.Fixed,
                Width = KeyColumnWidth
            },
            new Label(field.Value)
            {
                Color = Palette.Foreground,
                HorizontalAlign = HAlign.Left
            });

        return row;
    }
}