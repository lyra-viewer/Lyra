using Lyra.Imaging.Content.Status;
using Lyra.Imaging.Content.Textures;
using Lyra.Imaging.Content.Variants;
using Lyra.UI;
using Lyra.UI.Components;
using Lyra.UI.Components.Controls;
using Lyra.UI.Components.Controls.TreeView;
using Lyra.UI.Components.Layout;
using Lyra.UI.Components.Primitives;
using Lyra.UI.SupportingTypes;
using Lyra.UI.Theme;

namespace Lyra.GUI.Sections;

public sealed class TexturesSection : IUISection, IDisposable
{
    private const float LinkIconSize = 14f;

    private readonly Collapsible _collapsible;
    private readonly TreeView<TextureRow> _tree;
    private readonly Label _path;
    private readonly Label _foundPath;

    private VariantContainer? _set;
    private string? _modelDirectory;
    private List<TreeNode<TextureRow>> _roots = [];
    private int _lastVersion = -1;
    private int _lastActive = -1;

    public Collapsible Collapsible => _collapsible;

    public IComponent Root => _collapsible;

    /// <summary>Raised with the variant index of the texture the user picked.</summary>
    public event Action<int>? VariantSelected;

    public TexturesSection()
    {
        _tree = new TreeView<TextureRow>([], RenderRow)
        {
            Padding = new Padding(0, 4, 0, 0),
            VerticalSize = SizeMode.Flexible,
            IndentWidth = 16,
            RowSpacing = 2,
            CanPick = CanPick
        };

        _tree.Picked += OnPicked;

        _path = PathLabel();
        _foundPath = PathLabel();

        _collapsible = new Collapsible("TEXTURES")
            {
                HorizontalSize = SizeMode.Expand,
                Present = false
            }
            .Children(_tree, _path, _foundPath);
    }

    private static Label PathLabel() => new("")
    {
        Present = false,
        Color = Palette.Dim,
        Ellipsize = true,
        EllipsisPlacement = EllipsisPlacement.Middle,
        HorizontalSize = SizeMode.Expand,
        Padding = new Padding(4, 2, 0, 0)
    };

    public void Refresh(UIState state) => Refresh(state.Composite?.Content as VariantContainer, state.Composite?.FileInfo.DirectoryName);

    internal void Refresh(VariantContainer? set, string? modelDirectory = null)
    {
        if (set?.Textures is not { } layout)
        {
            if (_set is not null)
            {
                _roots = [];
                _tree.UpdateData([]);
                _set = null;
            }

            _collapsible.Present = false;
            return;
        }

        _collapsible.Present = true;

        if (!ReferenceEquals(set, _set))
        {
            _set = set;
            _modelDirectory = modelDirectory;
            _lastVersion = set.Version;
            _lastActive = -1;
            _roots = TextureTreeBuilder.Build(layout, set.Variants);
            _tree.UpdateData(_roots);
        }
        else if (set.Version != _lastVersion)
        {
            _lastVersion = set.Version;
            _tree.InvalidateRows();
        }

        var active = set.ActiveIndex;
        if (active != _lastActive)
        {
            _lastActive = active;

            // Another leaf may show the same texture; leave the pick where the user put it.
            if (_tree.PickedNode?.Data.Texture != active)
                _tree.Locate(row => row.Texture == active);

            OpenPickedGroup();

            ShowPaths(layout.SourceOf(active));
        }
    }

    internal IReadOnlyList<TreeNode<TextureRow>> Roots => _roots;

    internal TreeNode<TextureRow>? PickedNode => _tree.PickedNode;

    internal string PathText => _path.Present ? _path.Text : "";

    internal string FoundPathText => _foundPath.Present ? _foundPath.Text : "";

    internal bool CanPick(TreeNode<TextureRow> node) =>
        node.Data.Texture is { } texture && _set?.Textures is { } layout && texture < _set.Variants.Count && layout.SourceOf(texture)?.IsUnavailable != true;

    internal void Pick(TreeNode<TextureRow> node)
    {
        if (!CanPick(node))
            return;

        _tree.Locate(row => ReferenceEquals(row, node.Data));
        OnPicked(node);
    }

    private void OnPicked(TreeNode<TextureRow> node)
    {
        if (node.Data.Texture is not { } texture || _set?.Textures is not { } layout)
            return;

        _lastActive = texture;
        OpenPickedGroup();
        ShowPaths(layout.SourceOf(texture));

        VariantSelected?.Invoke(texture);
    }

    private void OpenPickedGroup()
    {
        if (_tree.PickedNode is not { IsLeaf: false, IsExpanded: false } group)
            return;

        group.IsExpanded = true;
        _tree.InvalidateRows();
    }
    
    private void ShowPaths(TextureSource? source)
    {
        _path.Text = source switch
        {
            { Location: TextureLocation.FoundByName, StoredPath: { } stored } => $"Stored   {stored}",
            { ResolvedPath: { } file } => $"Linked   {Shown(file, _modelDirectory)}",
            { StoredPath: { } stored } => $"Stored   {stored}",
            _ => ""
        };

        _foundPath.Text = source is { Location: TextureLocation.FoundByName, ResolvedPath: { } found }
            ? $"Found    {Shown(found, _modelDirectory)}"
            : "";

        _path.Present = _path.Text.Length > 0;
        _foundPath.Present = _foundPath.Text.Length > 0;
    }

    /// <summary>A file relative to the model's folder when inside it or one level up, otherwise in full.</summary>
    internal static string Shown(string file, string? modelDirectory)
    {
        if (string.IsNullOrEmpty(modelDirectory))
            return file;

        string relative;
        try
        {
            relative = Path.GetRelativePath(modelDirectory, file);
        }
        catch (ArgumentException)
        {
            return file;
        }

        // Another drive comes back rooted.
        if (Path.IsPathRooted(relative))
            return file;

        var up = relative.Split('/', '\\').TakeWhile(segment => segment == "..").Count();
        return up <= 1 ? relative : file;
    }

    private IComponent RenderRow(TreeNode<TextureRow> node, bool isPicked)
    {
        var row = node.Data;

        if (row.Count > 0 || row.Texture is not { } texture || _set?.Textures is not { } layout || texture >= _set.Variants.Count)
            return GroupRow(row, isPicked);

        var variant = _set.Variants[texture];
        var source = layout.SourceOf(texture);
        var failure = _set.FailureOf(texture);
        var unavailable = source?.IsUnavailable == true || failure is not null;
        var named = source is not null && !row.Title.Contains(variant.Label, StringComparison.Ordinal);

        var title = new Label(row.Title)
        {
            Color = unavailable ? Palette.Danger : isPicked ? Palette.SelectedForeground : Palette.Foreground,
            Ellipsize = true,
            EllipsisPlacement = EllipsisPlacement.Middle,
            HorizontalSize = SizeMode.Expand
        };

        var top = new HStack
            {
                Spacing = 8,
                HorizontalSize = SizeMode.Expand,
                Transient = true
            }
            .Children(title);

        if (named && !unavailable && variant is { Width: > 0, Height: > 0 })
            top.AddComponent(new Label($"{variant.Width} x {variant.Height}") { Color = Palette.Dim });

        var text = new VStack
            {
                HorizontalSize = SizeMode.Expand,
                Transient = true
            }
            .Children(top, DetailLabel(variant, source, failure, _set.WarningOf(texture), named));

        return new HStack
            {
                Spacing = 6,
                HorizontalSize = SizeMode.Expand,
                VerticalAlign = VAlign.Center,
                Padding = new Padding(4, 2, 8, 2)
            }
            .Children(LinkColumn(source), text);
    }

    /// <summary>The second line: what is wrong with the texture, else its file's name or its description.</summary>
    private static Label DetailLabel(ImageVariant variant, TextureSource? source, LoadFailure? failure, LoadWarning? warning, bool named)
    {
        var detail = source is { Location: TextureLocation.Missing, StoredPath: { } stored }
            ? new Label(stored) { Color = Palette.Danger }
            : failure is not null
                ? new Label(failure.Message) { Color = Palette.Danger }
                : warning is not null
                    ? new Label(warning.Detail) { Color = Palette.Warning }
                    : new Label(named ? variant.Label : variant.Detail) { Color = Palette.Dim };

        detail.Ellipsize = true;
        detail.EllipsisPlacement = EllipsisPlacement.Middle;
        detail.HorizontalSize = SizeMode.Expand;
        return detail;
    }

    private static IComponent LinkColumn(TextureSource? source) =>
        source is not null && LinkColor(source.Location) is { } tint
            ? new SvgImage(ResourceLoader.GetSvg("link"), LinkIconSize, LinkIconSize)
            {
                Tint = tint,
                VerticalAlign = VAlign.Center
            }
            : new HStack
            {
                HorizontalSize = SizeMode.Fixed,
                Width = LinkIconSize
            };

    private static IComponent GroupRow(TextureRow row, bool isPicked) =>
        new HStack
            {
                Spacing = 8,
                HorizontalSize = SizeMode.Expand,
                VerticalAlign = VAlign.Center,
                Padding = new Padding(0, 2, 8, 2)
            }
            .Children(
                new Label(row.Title)
                {
                    Color = isPicked ? Palette.SelectedForeground : Palette.Foreground,
                    Ellipsize = true,
                    HorizontalSize = SizeMode.Expand
                },
                new Label(row.Count.ToString()) { Color = Palette.Muted });

    private static SkiaSharp.SKColor? LinkColor(TextureLocation location) => location switch
    {
        TextureLocation.Linked => Palette.Dim,
        TextureLocation.FoundByName => Palette.Warning,
        TextureLocation.Missing => Palette.Danger,
        _ => null
    };

    public void Dispose()
    {
        _tree.Picked -= OnPicked;
        _tree.Dispose();
    }
}