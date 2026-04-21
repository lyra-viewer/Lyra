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
//  ExifSection - sidebar collapsible showing EXIF metadata as a key/value table.
// ----------------------------------------------------------------------------
//  Shares its key column with FormatSection via KeyColumnRegistry so the
//  two sections form a single visual grid.
//
//  Refresh has two phases:
//    1. Always report key widths to the registry (cheap, dedup-independent).
//    2. Only rebuild the ListView data when the composite identity or state
//       has changed (expensive).
//
//  When EXIF is missing or invalid, shows a status label explaining why.
// ============================================================================
public sealed class ExifSection : IUISection
{
    public const string KeyColumn = "metadata";
    private const bool AllowSeparators = true;

    private readonly KeyColumnRegistry _registry;

    private readonly Collapsible _collapsible;
    private readonly Label _statusLabel;
    private readonly ListView<ExifEntry> _list;

    private Composite? _lastComposite;
    private CompositeState _lastState;

    public Collapsible Collapsible => _collapsible;

    public IComponent Root => _collapsible;

    public ExifSection(KeyColumnRegistry registry)
    {
        _registry = registry;

        _statusLabel = new Label("")
        {
            Present = false,
            Color = Palette.Danger,
            Padding = new Padding(0, 4, 0, 0)
        };

        _list = new ListView<ExifEntry>([], RenderRow)
        {
            Padding = new Padding(0, 4, 0, 0),
            VerticalSize = SizeMode.Flexible,
            CanPick = e => !e.IsSeparator
        };

        _collapsible = new Collapsible("EXIF METADATA")
        {
            HorizontalSize = SizeMode.Expand,
            Present = false
        };
        _collapsible.AddComponents(_statusLabel, _list);
    }

    public void Refresh(UIState state)
    {
        var composite = state.Composite;

        // Phase 1: report widths unconditionally so the registry stays
        // consistent regardless of whether we end up rebuilding data below.
        ReportWidths(composite);

        // Phase 2: dedup the expensive rebuild on (composite, state).
        if (composite == _lastComposite && state.CompositeState == _lastState)
            return;

        _lastComposite = composite;
        _lastState = state.CompositeState;

        ApplyData(composite);
    }

    private void ReportWidths(Composite? composite)
    {
        if (composite?.ExifInfo is not { } exif || !exif.IsValid() || !exif.HasData())
            return;

        foreach (var entry in exif.ToKeyValuePairs())
        {
            if (entry.IsSeparator)
                continue;

            _registry.Report(KeyColumn, Label.MeasureTextWidth(entry.Key));
        }
    }

    private void ApplyData(Composite? composite)
    {
        if (composite?.ExifInfo is { } exif && exif.IsValid() && exif.HasData())
        {
            var pairs = exif.ToKeyValuePairs()
                .Where(e => !e.IsSeparator || AllowSeparators)
                .ToList();

            _list.UpdateData(pairs);
            _statusLabel.Present = false;
            _collapsible.Present = true;
            return;
        }

        _list.UpdateData([]);
        _collapsible.Present = composite != null;
        _statusLabel.Present = composite != null;
        _statusLabel.Text = composite switch
        {
            null => "",
            _ when composite.ExifInfo is null => "No EXIF metadata.",
            _ when !composite.ExifInfo.IsValid() => "Failed to parse EXIF metadata.",
            _ => "No recognized EXIF metadata."
        };
    }

    private HStack RenderRow(ExifEntry item, bool isPicked)
    {
        var row = new HStack
        {
            Spacing = 8,
            HorizontalSize = SizeMode.Expand
        };

        if (item.IsSeparator)
        {
            row.VerticalSize = SizeMode.Fixed;
            row.Height = 10;
            row.Transient = true;
            return row;
        }

        row.AddComponents(
            new Label(item.Key)
            {
                Color = Palette.Dim,
                HorizontalSize = SizeMode.Fixed,
                Width = _registry.Get(KeyColumn)
            },
            new Label(item.Value) { Color = Palette.Foreground });
        
        return row;
    }
}