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
//  FormatSection - sidebar collapsible showing format-specific metadata.
// ----------------------------------------------------------------------------
//  Same key-column registry pattern as ExifSection: the two sections share
//  the "metadata" column id, so their key columns visually align.
//
//  Title is dynamic - includes the image format type so it reads as e.g.
//  "FORMAT SPECIFIC [PSD]" or "FORMAT SPECIFIC [JPEG]".
// ============================================================================
public sealed class FormatSection : IUISection
{
    private readonly KeyColumnRegistry _registry;

    private readonly Collapsible _collapsible;
    private readonly ListView<KeyValuePair<string, string>> _list;

    private Composite? _lastComposite;
    private CompositeState _lastState;
    private int _lastCount = -1;
    private float _keyWidth;

    public Collapsible Collapsible => _collapsible;

    public IComponent Root => _collapsible;

    public FormatSection(KeyColumnRegistry registry)
    {
        _registry = registry;

        _list = new ListView<KeyValuePair<string, string>>([], RenderRow)
        {
            Padding = new Padding(0, 4, 0, 0),
            VerticalSize = SizeMode.Flexible
        };

        _collapsible = new Collapsible("FORMAT SPECIFIC")
            {
                HorizontalSize = SizeMode.Expand,
                Present = false
            }
            .Child(_list);
    }

    public void Refresh(UIState state)
    {
        var composite = state.Composite;
        var metadata = composite?.FormatSpecificSnapshot() ?? [];

        // Decoders keep adding entries while the composite identity and state stay put,
        // so the entry count takes part in the dedup key.
        if (composite != _lastComposite || state.CompositeState != _lastState || metadata.Count != _lastCount)
        {
            _lastComposite = composite;
            _lastState = state.CompositeState;
            _lastCount = metadata.Count;

            ApplyData(composite, metadata);
            _keyWidth = MeasureKeyWidth(metadata);
        }

        _registry.Report(ExifSection.KeyColumn, _keyWidth);
    }

    private static float MeasureKeyWidth(List<KeyValuePair<string, string>> metadata)
    {
        var width = 0f;

        foreach (var pair in metadata)
            width = Math.Max(width, Label.MeasureTextWidth(pair.Key));

        return width;
    }

    private void ApplyData(Composite? composite, List<KeyValuePair<string, string>> metadata)
    {
        if (composite != null && metadata.Count > 0)
        {
            _collapsible.Present = true;
            _collapsible.Title = $"FORMAT SPECIFIC [{composite.ImageFormatType}]";
            _list.UpdateData(metadata);
        }
        else
        {
            _collapsible.Present = false;
            _list.UpdateData([]);
        }
    }

    private HStack RenderRow(KeyValuePair<string, string> item, bool isPicked)
    {
        return new HStack
            {
                Spacing = 8,
                HorizontalSize = SizeMode.Expand
            }
            .Children(
                new Label(item.Key)
                {
                    Color = Palette.Dim,
                    HorizontalSize = SizeMode.Fixed,
                    Width = _registry.Get(ExifSection.KeyColumn)
                },
                new Label(item.Value)
                {
                    Color = Palette.Foreground,
                    HorizontalAlign = HAlign.Left
                });
    }
}