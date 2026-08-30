using Lyra.Common;
using Lyra.Imaging.Content;
using Lyra.UI.Components;
using Lyra.UI.Components.Controls;
using Lyra.UI.Components.Layout;
using Lyra.UI.Components.Primitives;
using Lyra.UI.SupportingTypes;
using Lyra.UI.Theme;

namespace Lyra.Renderer.GUI.Sections;

public sealed class VariantsSection : IUISection, IDisposable
{
    private readonly Collapsible _collapsible;
    private readonly ListView<ImageVariant> _list;

    private IReadOnlyList<ImageVariant>? _lastVariants;
    private int _lastActive = -1;

    public Collapsible Collapsible => _collapsible;

    public IComponent Root => _collapsible;

    /// <summary>Raised with the index the user picked in the current variant list.</summary>
    public event Action<int>? VariantSelected;

    public VariantsSection()
    {
        _list = new ListView<ImageVariant>([], RenderRow)
        {
            HorizontalSize = SizeMode.Expand,
            VerticalSize = SizeMode.Flexible,
            RowSpacing = 4f,
            Padding = new Padding(0, 4, 0, 0),
            PickedBackground = Palette.Transparent,
            CanPick = _ => true
        };

        _list.Picked += OnPicked;

        _collapsible = new Collapsible("VARIANTS")
            {
                HorizontalSize = SizeMode.Expand,
                Present = false
            }
            .Child(_list);
    }

    public void Refresh(UIState state)
    {
        // The variant set is a property of the content, not of the document.
        var set = state.Composite?.Content as VariantRasterContent;
        var variants = set?.Variants;

        if (variants is null || variants.Count == 0)
        {
            if (_lastVariants is not null)
            {
                _list.UpdateData([]);
                _lastVariants = null;
                _lastActive = -1;
            }

            _collapsible.Present = false;
            return;
        }

        _collapsible.Present = true;

        if (!ReferenceEquals(variants, _lastVariants))
        {
            _lastVariants = variants;
            _lastActive = -1;
            _list.UpdateData([..variants]);
        }
        
        var active = set!.ActiveIndex;
        if (active != _lastActive)
        {
            _lastActive = active;
            var target = variants[active];
            _list.Locate(v => ReferenceEquals(v, target));
        }
    }

    private void OnPicked(ImageVariant variant)
    {
        var index = IndexOf(_lastVariants, variant);
        if (index < 0)
            return;

        _lastActive = index;
        VariantSelected?.Invoke(index);
    }

    /// <summary>Applies a pick. Internal so tests can drive it without a real click.</summary>
    internal void Select(ImageVariant variant) => OnPicked(variant);
    
    private static int IndexOf(IReadOnlyList<ImageVariant>? variants, ImageVariant variant)
    {
        if (variants is null)
            return -1;

        for (var i = 0; i < variants.Count; i++)
            if (ReferenceEquals(variants[i], variant))
                return i;

        return -1;
    }

    internal static HStack RenderRow(ImageVariant variant, bool isPicked)
    {
        var titleColumn = new VStack
        {
            HorizontalSize = SizeMode.Expand,
            Transient = true
        };

        titleColumn.AddComponent(new Label(variant.Label)
        {
            Color = isPicked ? Palette.SelectedForeground : Palette.Foreground
        });

        titleColumn.AddComponent(new Label(variant.Detail)
        {
            Color = Palette.Dim
        });

        return new HStack
            {
                Spacing = 8,
                HorizontalSize = SizeMode.Expand,
                VerticalAlign = VAlign.Center,
                Padding = new Padding(8, 4, 8, 4),
                BackgroundColor = isPicked ? Palette.SelectedBackground : Palette.Subtle
            }
            .Children(
                titleColumn,
                new Label(Formatters.SizeToStr(variant.ByteSize))
                { Color = Palette.Muted });
    }

    public void Dispose()
    {
        _list.Picked -= OnPicked;
        _list.Dispose();
    }
}