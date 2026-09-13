using Lyra.Common;
using Lyra.Imaging.Content;
using Lyra.Renderer.GUI.Support;
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
//  key-value fields. Populated for DDS, KTX and PSD; any decoder that fills
//  Composite.Structure participates automatically.
// ============================================================================
public sealed class StructureSection : IUISection
{
    private const string KeyColumn = "structure";

    private readonly KeyColumnRegistry _registry;

    private readonly Collapsible _collapsible;
    private readonly VScrollContainer _groups;

    private Composite? _lastComposite;
    private CompositeState _lastState;

    public Collapsible Collapsible => _collapsible;

    public IComponent Root => _collapsible;

    public StructureSection(KeyColumnRegistry registry)
    {
        _registry = registry;

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
            }
            .Child(_groups);
    }

    public void Refresh(UIState state)
    {
        var composite = state.Composite;
        
        ReportWidths(composite);
        
        if (composite == _lastComposite && state.CompositeState == _lastState)
            return;

        _lastComposite = composite;
        _lastState = state.CompositeState;

        ApplyData(composite);
    }

    private void ReportWidths(Composite? composite)
    {
        var structure = composite?.Structure;
        if (structure is null)
            return;

        foreach (var group in structure)
        foreach (var field in group.Fields)
            _registry.Report(KeyColumn, Label.MeasureTextWidth(field.Key));
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

    private Collapsible BuildGroupPanel(StructureGroup group)
    {
        var titleColumn = new VStack { HorizontalSize = SizeMode.Expand, Transient = true };
        titleColumn.AddComponent(new Label(group.Name) { Color = Palette.Foreground, Bold = false });

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

        // Shrink vertically so the outer VScrollContainer owns scrolling and the
        // ListView never grows its own scrollbar (no nested double-scroll).
        var list = new ListView<KeyValuePair<string, string>>([..group.Fields], RenderRow)
        {
            HorizontalSize = SizeMode.Expand,
            VerticalSize = SizeMode.Shrink,
            CanPick = _ => true
        };

        panel.AddComponent(list);
        return panel;
    }

    private HStack RenderRow(KeyValuePair<string, string> field, bool isPicked)
    {
        return new HStack
            {
                Spacing = 8,
                HorizontalSize = SizeMode.Expand,
                Padding = new Padding(16, 1, 0, 1)
            }
            .Children(
                new Label(field.Key)
                {
                    Color = Palette.Dim,
                    HorizontalSize = SizeMode.Fixed,
                    Width = _registry.Get(KeyColumn)
                },
                new Label(field.Value)
                {
                    Color = Palette.Foreground,
                    HorizontalAlign = HAlign.Left
                });
    }
}