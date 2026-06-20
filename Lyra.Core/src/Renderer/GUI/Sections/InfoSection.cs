using Lyra.Common;
using Lyra.Common.SystemExtensions;
using Lyra.Imaging.Content;
using Lyra.UI.Components;
using Lyra.UI.Components.Layout;
using Lyra.UI.Components.Primitives;
using Lyra.UI.SupportingTypes;
using Lyra.UI.Theme;

namespace Lyra.Renderer.GUI.Sections;

public sealed class InfoSection : IUISection
{
    private readonly VStack _root;

    // File row
    private readonly HStack _fileRow;
    private readonly Label _collectionLabel;
    private readonly Label _dirNavLabel;
    private readonly Label _fileNameLabel;
    private readonly Label _fileSizeLabel;

    // Image row
    private readonly HStack _imageRow;
    private readonly Label _formatLabel;
    private readonly Label _dimensionsLabel;

    // Collection row
    private readonly HStack _collectionRow;
    private readonly Label _collectionTypeLabel;
    private readonly Label _directoryNameLabel;
    
    // Displaying row
    private readonly HStack _displayingRow;
    private readonly Label _zoomLabel;
    private readonly Label _displayModeLabel;
    private readonly Label _samplingModeLabel;

    // Large image row
    private readonly HStack _largeImageRow;
    private readonly Label _largeImagePreviewLabel;
    private readonly Label _largeImageTilesLabel;

    public IComponent Root => _root;

    public InfoSection()
    {
        _fileRow = BuildRow();
        _fileRow.AddComponents(
            DimPrefix("[File]        "),
            _collectionLabel = ForegroundLabel(),
            _dirNavLabel = HighlightedLabel(),
            Separator(),
            _fileNameLabel = ForegroundLabel(),
            Separator(),
            _fileSizeLabel = ForegroundLabel()
        );

        _imageRow = BuildRow();
        _imageRow.AddComponents(
            DimPrefix("[Image]       "),
            _formatLabel = ForegroundLabel(),
            Separator(),
            _dimensionsLabel = ForegroundLabel()
        );

        _collectionRow = BuildRow();
        _collectionRow.AddComponents(
            DimPrefix("[Collection]  "),
            _collectionTypeLabel = ForegroundLabel(),
            Separator(),
            DimPrefix("Dir:"),
            _directoryNameLabel = ForegroundLabel()
        );

        _displayingRow = BuildRow();
        _displayingRow.AddComponents(
            DimPrefix("[Displaying]  "),
            DimPrefix("Zoom:"),
            _zoomLabel = ForegroundLabel(),
            Separator(),
            DimPrefix("Display Mode:"),
            _displayModeLabel = ForegroundLabel(),
            Separator(),
            DimPrefix("Sampling:"),
            _samplingModeLabel = ForegroundLabel()
        );
        
        _largeImageRow = BuildRow();
        _largeImageRow.AddComponents(
            DimPrefix("[Large Image] "),
            DimPrefix("Preview:"),
            _largeImagePreviewLabel = ForegroundLabel(),
            Separator(),
            DimPrefix("Tiles:"),
            _largeImageTilesLabel = ForegroundLabel()
        );

        _root = new VStack
        {
            HorizontalSize = SizeMode.Expand,
            VerticalSize = SizeMode.Shrink,
            HorizontalAlign = HAlign.Left,
            Spacing = 2,
            Padding = new Padding(8)
        };
        _root.AddComponents(_fileRow, _imageRow, _collectionRow, _displayingRow, _largeImageRow);
    }

    public void Refresh(UIState state)
    {
        if (state.Composite == null)
        {
            _fileRow.Present = false;
            _imageRow.Present = false;
            _displayingRow.Present = false;
            _collectionRow.Present = false;
            _largeImageRow.Present = false;
            return;
        }

        _fileRow.Present = true;
        _imageRow.Present = true;
        _displayingRow.Present = true;
        _collectionRow.Present = true;

        var composite = state.Composite;
        var fileInfo = composite.FileInfo;
        var app = state.AppStates;

        // File row
        _collectionLabel.Text = $"{app.CollectionIndex}/{app.CollectionCount}";

        if (app is { DirectoryCount: not null, DirectoryIndex: not null })
        {
            _dirNavLabel.Present = true;
            _dirNavLabel.Text = $"({app.DirectoryIndex}/{app.DirectoryCount})";
        }
        else
        {
            _dirNavLabel.Present = false;
        }

        _fileNameLabel.Text = fileInfo.Name;
        _fileSizeLabel.Text = Formatters.SizeToStr(fileInfo.Length);

        // Image row
        _formatLabel.Text = composite.ImageFormatType.Description();
        _dimensionsLabel.Text = $"{composite.LogicalWidth}x{composite.LogicalHeight}";

        // Displaying row
        _zoomLabel.Text = $"{app.Zoom}%";
        _displayModeLabel.Text = app.DisplayMode.Description();
        _samplingModeLabel.Text = app.SamplingMode;

        // Collection row
        _collectionTypeLabel.Text = app.CollectionType.Description();
        _directoryNameLabel.Text = $"{fileInfo.DirectoryName}/";

        // Large image row
        if (composite.Content is RasterLargeContent large)
        {
            _largeImageTilesLabel.Text = (large.TilesTotal != null)
                ? $"{large.TilesReady}/{large.TilesTotal}"
                : "Calculating";

            _largeImagePreviewLabel.Text = composite.State is CompositeState.Ready or CompositeState.Complete
                ? "Ready"
                : composite.State.ToDisplayString();

            _largeImageRow.Present = true;
        }
        else
        {
            _largeImageRow.Present = false;
        }
    }

    // ----- helpers -----------------------------------------------------------

    private static HStack BuildRow() => new()
    {
        Spacing = 11,
        HorizontalSize = SizeMode.Expand,
        HorizontalAlign = HAlign.Left
    };

    private static Label DimPrefix(string text) => new(text)
    {
        Color = Palette.Dim,
        Transient = true
    };

    private static Label Separator() => new("|")
    {
        Color = Palette.Dim,
        Transient = true
    };

    private static Label ForegroundLabel() => new("")
    {
        Color = Palette.Foreground,
        Transient = true
    };

    private static Label HighlightedLabel() => new("")
    {
        Color = Palette.SelectedForeground,
        Transient = true
    };
}