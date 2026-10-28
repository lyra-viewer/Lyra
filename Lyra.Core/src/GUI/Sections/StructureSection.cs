using Lyra.Common;
using Lyra.Imaging.Content;
using Lyra.Imaging.Content.Status;
using Lyra.UI.Components;
using Lyra.UI.Components.Controls;
using Lyra.UI.Components.Layout;
using Lyra.UI.Components.Primitives;
using Lyra.UI.SupportingTypes;
using Lyra.UI.Theme;

namespace Lyra.GUI.Sections;

public sealed class StructureSection : IUISection
{
    /// <summary>How far each level of a nested structure is indented.</summary>
    private const int DepthIndent = 14;

    /// <summary>The widest a part's key column grows, in characters; longer keys end in an ellipsis.</summary>
    private const int MaxKeyCharacters = 32;

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
            }
            .Child(_groups);
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
        var titleColumn = new VStack { HorizontalSize = SizeMode.Expand, Transient = true };
        titleColumn.AddComponent(new Label(group.Name) { Color = Palette.Foreground, Bold = false, HorizontalSize = SizeMode.Expand, Ellipsize = true });

        if (!string.IsNullOrEmpty(group.Description))
            titleColumn.AddComponent(new Label(group.Description) { Color = Palette.Dim, HorizontalSize = SizeMode.Expand, Ellipsize = true });

        var header = new HStack
        {
            Spacing = 8,
            HorizontalSize = SizeMode.Expand,
            VerticalAlign = VAlign.Top,
            Padding = new Padding(0, 2, 0, 2)
        };
        header.AddComponent(titleColumn);

        // Offset beside the name, size beside the description: a narrow column leaves the name room.
        var placement = new VStack { HorizontalSize = SizeMode.Shrink, Transient = true };

        if (group.Offset is { } offset)
            placement.AddComponent(new Label($"0x{offset:X}") { Color = Palette.Muted, HorizontalAlign = HAlign.Right });

        if (group.SizeBytes.HasValue)
            placement.AddComponent(new Label(Formatters.SizeToStr(group.SizeBytes.Value)) { Color = Palette.Muted, HorizontalAlign = HAlign.Right });

        if (placement.Children.Count > 0)
            header.AddComponent(placement);

        var panel = new Collapsible(header)
        {
            HorizontalSize = SizeMode.Expand,
            Padding = new Padding(10 + DepthIndent * group.Depth, 0, 0, 0),
            Expandable = group.Fields.Count > 0
        };

        if (group.Fields.Count == 0)
            return panel;

        var keyWidth = KeyWidth(group);

        // Shrink vertically so the outer VScrollContainer owns scrolling and the
        // ListView never grows its own scrollbar (no nested double-scroll).
        var list = new ListView<KeyValuePair<string, string>>([..group.Fields], (field, _) => RenderRow(field, keyWidth))
        {
            HorizontalSize = SizeMode.Expand,
            VerticalSize = SizeMode.Shrink,
            CanPick = _ => true
        };

        panel.AddComponent(list);
        return panel;
    }

    /// <summary>The part's own key column: as wide as its longest key, up to <see cref="MaxKeyCharacters"/>.</summary>
    private static float KeyWidth(StructureGroup group)
    {
        var longest = 0f;
        foreach (var field in group.Fields)
            longest = Math.Max(longest, Label.MeasureTextWidth(field.Key));

        return Math.Min(longest, Label.MeasureTextWidth(new string('M', MaxKeyCharacters)));
    }

    private static HStack RenderRow(KeyValuePair<string, string> field, float keyWidth)
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
                    Width = keyWidth,
                    Ellipsize = true
                },
                new Label(field.Value)
                {
                    Color = Palette.Foreground,
                    HorizontalAlign = HAlign.Left,
                    HorizontalSize = SizeMode.Expand,
                    Ellipsize = true
                });
    }
}