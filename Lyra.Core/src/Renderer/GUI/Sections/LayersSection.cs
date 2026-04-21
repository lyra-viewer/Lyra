using Lyra.Imaging.Content;
using Lyra.Psd.Core.Decode.Layers;
using Lyra.UI;
using Lyra.UI.Components;
using Lyra.UI.Components.Controls;
using Lyra.UI.Components.Controls.TreeView;
using Lyra.UI.Components.Layout;
using Lyra.UI.Components.Primitives;
using Lyra.UI.SupportingTypes;
using Lyra.UI.Theme;

namespace Lyra.Renderer.GUI.Sections;

// ============================================================================
//  LayersSection - sidebar collapsible showing the PSD layer hierarchy.
// ----------------------------------------------------------------------------
//  Only visible for PSD composites. PsdLayers is null until the decoder has
//  walked the layer table; the (composite, state) dedup catches the moment
//  it becomes available.
//
//  The group hierarchy is reconstructed from lsct section dividers by
//  PsdLayerTreeBuilder. The TreeView is display-only here - no Locate or
//  Picked wiring, since clicking a PSD layer has no companion action.
// ============================================================================
public sealed class LayersSection : IUISection
{
    private const float VisibilityIconSize = 14f;

    private readonly Collapsible _collapsible;
    private readonly Label _statusLabel;
    private readonly TreeView<LayerRecord> _tree;

    private Composite? _lastComposite;
    private CompositeState _lastState;

    public Collapsible Collapsible => _collapsible;

    public IComponent Root => _collapsible;

    public LayersSection()
    {
        _statusLabel = new Label("No layers.")
        {
            Present = false,
            Color = Palette.Danger,
            Padding = new Padding(0, 4, 0, 0)
        };

        _tree = new TreeView<LayerRecord>([], RenderRow)
        {
            Padding = new Padding(0, 4, 0, 0),
            VerticalSize = SizeMode.Flexible,
            IndentWidth = 16
        };

        _collapsible = new Collapsible("PSD LAYERS")
        {
            HorizontalSize = SizeMode.Expand,
            Present = false
        };
        _collapsible.AddComponents(_statusLabel, _tree);
    }

    public void Refresh(UIState state)
    {
        var composite = state.Composite;

        if (composite == _lastComposite && state.CompositeState == _lastState)
            return;

        _lastComposite = composite;
        _lastState = state.CompositeState;

        if (composite?.PsdLayers != null)
        {
            _collapsible.Present = true;

            if (composite.PsdLayers.Length > 0)
            {
                _statusLabel.Present = false;
                _tree.UpdateData(PsdLayerTreeBuilder.Build(composite.PsdLayers));
            }
            else
            {
                _statusLabel.Present = true;
                _tree.UpdateData([]);
            }
        }
        else
        {
            _collapsible.Present = false;
            _statusLabel.Present = false;
            _tree.UpdateData([]);
        }
    }

    private static IComponent RenderRow(TreeNode<LayerRecord> node, bool isPicked)
    {
        var record = node.Data;
        var isGroup = record.SectionType
            is LayerSectionType.OpenGroup
            or LayerSectionType.ClosedGroup;

        var name = string.IsNullOrEmpty(record.Name) ? "[unnamed]" : record.Name;
        var displayName = isGroup
            ? $"{name} [{node.Children.Count}]"
            : name;

        var color = !record.Visible
            ? Palette.Muted
            : isPicked
                ? Palette.SelectedForeground
                : Palette.Foreground;

        var label = new Label(displayName)
        {
            Color = color,
            HorizontalSize = SizeMode.Expand,
            VerticalAlign = VAlign.Center
        };

        // Visible layers: plain label - cheapest row, no HStack wrapper.
        if (record.Visible)
            return label;

        // Hidden layers: label (Expand) + trailing visibility_off icon.
        // Expand pushes the icon to the right edge.
        var row = new HStack
        {
            HorizontalSize = SizeMode.Expand,
            Spacing = 4
        };

        var icon = new SvgImage(ResourceLoader.GetSvg("visibility_off"), VisibilityIconSize, VisibilityIconSize);
        icon.VerticalAlign = VAlign.Center;
        icon.Padding = new Padding(0, 0, 12, 0);

        row.AddComponents(label, icon);
        return row;
    }
}