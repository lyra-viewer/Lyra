using Lyra.FileLoader.Navigation;
using Lyra.Renderer.GUI.Presenters;
using Lyra.UI;
using Lyra.UI.Components;
using Lyra.UI.Components.Controls;
using Lyra.UI.Components.Controls.TreeView;
using Lyra.UI.Components.Primitives;
using Lyra.UI.SupportingTypes;
using Lyra.UI.Theme;

namespace Lyra.Renderer.GUI.Sections;

// ============================================================================
//  DirectoryTreeSection - sidebar collapsible with the navigable directory tree.
// ----------------------------------------------------------------------------
//  Owns the TreeView<DirEntry> and the DirectoryTreePresenter that
//  translates DirectorySnapshot into tree state. Picking flow:
//
//    TreeView.Picked -> presenter.HandlePicked -> presenter.DirectoryPicked
//      -> section.DirectoryPicked -> UIManager.DirectoryPicked -> SdlCore
//
//  This section is IDisposable because it owns the folder icons (loaded
//  from embedded SVGs at construction).
// ============================================================================
public sealed class DirectoryTreeSection : IUISection, IDisposable
{
    private const float DirIconSize = 16f;

    private readonly DirectoryTreePresenter _presenter;
    private readonly TreeView<DirEntry> _treeView;
    private readonly Collapsible _collapsible;

    // Loaded but currently unused - IconProvider wiring is commented in the
    // original code. Kept here so the wiring can be re-enabled without
    // rediscovering the resource paths.
    private readonly SvgImage _folderIcon;
    private readonly SvgImage _folderOpenIcon;
    private readonly SvgImage _folderOffIcon;

    /// <summary>
    /// Fired when the user picks a directory containing images.
    /// Parameter is the absolute directory path.
    /// </summary>
    public event Action<string>? DirectoryPicked;

    public Collapsible Collapsible => _collapsible;

    public IComponent Root => _collapsible;

    public DirectoryTreeSection()
    {
        _folderIcon     = new SvgImage(ResourceLoader.GetSvg("folder"),      DirIconSize, DirIconSize);
        _folderOpenIcon = new SvgImage(ResourceLoader.GetSvg("folder_open"), DirIconSize, DirIconSize);
        _folderOffIcon  = new SvgImage(ResourceLoader.GetSvg("folder_off"),  DirIconSize, DirIconSize);

        _treeView = new TreeView<DirEntry>([], RenderNode)
        {
            HorizontalSize = SizeMode.Expand,
            VerticalSize = SizeMode.Flexible
        };

        // _treeView.IconProvider = node =>
        // {
        //     if (!node.Data.HasImages)
        //         return _folderOffIcon;
        //
        //     return node.IsExpanded ? _folderOpenIcon : _folderIcon;
        // };

        _presenter = new DirectoryTreePresenter();
        _presenter.DirectoryPicked += path => DirectoryPicked?.Invoke(path);
        _treeView.Picked += _presenter.HandlePicked;

        _collapsible = new Collapsible("DIRECTORIES")
        {
            HorizontalSize = SizeMode.Expand,
            Present = false, 
            IsExpanded = true
        };
        _collapsible.AddComponent(_treeView);
    }

    public void Refresh(UIState state)
    {
        _presenter.Apply(state.Directories, _treeView);
    }

    public void Dispose()
    {
        _folderIcon.Dispose();
        _folderOpenIcon.Dispose();
        _folderOffIcon.Dispose();
    }

    private static Label RenderNode(TreeNode<DirEntry> node, bool isPicked) =>
        new(node.Data.CollapsedDisplayName ?? PathTreeBuilder.DisplayName(node.Data.Path))
        {
            Padding = new Padding(0, 1, 0, 1),
            Color = isPicked ? Palette.SelectedForeground
                : node.Data.HasImages ? Palette.Foreground
                : Palette.Dim
        };
}