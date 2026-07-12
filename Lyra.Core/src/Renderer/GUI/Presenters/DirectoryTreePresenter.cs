using Lyra.FileLoader.Navigation;
using Lyra.UI.Components.Controls.TreeView;

namespace Lyra.Renderer.GUI.Presenters;

// ============================================================================
//  DirectoryTreePresenter - converts DirectorySnapshot into TreeView state.
// ----------------------------------------------------------------------------
//  Owns the relative-path/absolute-path mapping and the version/selection
//  dedup. DirectoryTreeSection delegates all directory logic here.
//
//  Lifecycle per Apply:
//    1. If snapshot.Version changed -> rebuild the tree, clear last selection.
//    2. If snapshot.CurrentDirectory changed -> update tree pick state.
//
//  Picking flow:
//    TreeView fires Picked -> Presenter resolves relative->absolute path ->
//    DirectoryPicked event fires -> DirectoryTreeSection re-emits -> UIManager
//    re-emits -> SdlCore navigates.
// ============================================================================
public sealed class DirectoryTreePresenter
{
    private readonly Dictionary<string, string> _directoryMap = new();
    private int _lastVersion = -1;
    private string? _lastCurrentDirectory;

    /// <summary>
    /// Fired when the user picks a directory in the TreeView.
    /// Parameter is the normalized absolute directory path.
    /// </summary>
    public event Action<string>? DirectoryPicked;

    /// <summary>
    /// Wire to TreeView.Picked. Resolves the picked node to a directory
    /// that actually holds images and re-emits DirectoryPicked.
    ///
    /// If the picked directory has no images of its own but a descendant
    /// does (a collapsed folder that only "leads to" images), jump to the
    /// closest descendant that holds images instead of doing nothing.
    /// </summary>
    public void HandlePicked(TreeNode<DirEntry> node)
    {
        var target = node.Data.HasImages ? node : FindClosestWithImages(node);
        if (target == null)
            return;

        if (_directoryMap.TryGetValue(target.Data.Path, out var absolutePath))
            DirectoryPicked?.Invoke(absolutePath);
    }

    /// <summary>
    /// Breadth-first search for the shallowest descendant holding images.
    /// BFS keeps "closest" meaning nearest by depth, and among equal depths
    /// respects the tree's existing (sorted) child order.
    /// </summary>
    private static TreeNode<DirEntry>? FindClosestWithImages(TreeNode<DirEntry> node)
    {
        var queue = new Queue<TreeNode<DirEntry>>(node.Children);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current.Data.HasImages)
                return current;

            foreach (var child in current.Children)
                queue.Enqueue(child);
        }

        return null;
    }

    /// <summary>
    /// Apply a snapshot to the given TreeView. Returns true if anything
    /// changed (caller can use this to invalidate).
    /// </summary>
    public bool Apply(DirectorySnapshot snapshot, TreeView<DirEntry> treeView)
    {
        var changed = false;

        if (snapshot.Version != _lastVersion)
        {
            _lastVersion = snapshot.Version;
            RebuildTree(snapshot.TopDirectory, snapshot.Entries, treeView);
            _lastCurrentDirectory = null;
            changed = true;
        }

        if (snapshot.CurrentDirectory != _lastCurrentDirectory)
        {
            _lastCurrentDirectory = snapshot.CurrentDirectory;
            UpdateSelection(snapshot.CurrentDirectory, treeView);
            changed = true;
        }

        return changed;
    }

    private void RebuildTree(string? topDirectory, IReadOnlyList<DirEntry> entries, TreeView<DirEntry> treeView)
    {
        _directoryMap.Clear();

        if (entries.Count == 0)
        {
            treeView.UpdateData([]);
            return;
        }

        // Use topDirectory's parent as base so the top dir name becomes the root node.
        // For multi-directory drops without topDirectory, find the common root.
        var allPaths = entries.Select(e => e.Path).ToList();
        var baseDir = topDirectory != null
            ? Path.GetDirectoryName(topDirectory) ?? topDirectory
            : FindCommonRoot(allPaths);

        var relativeEntries = new List<DirEntry>();

        foreach (var entry in entries)
        {
            var rel = Path.GetRelativePath(baseDir, entry.Path)
                .Replace(Path.DirectorySeparatorChar, '/');

            if (rel is "." or "")
                continue;

            relativeEntries.Add(entry with { Path = rel });
            _directoryMap[rel] = entry.Path;
        }

        relativeEntries.Sort((a, b) => string.Compare(a.Path, b.Path, StringComparison.Ordinal));

        var roots = PathTreeBuilder.Build(relativeEntries, e => e.Path);

        roots = PathTreeBuilder.CollapseEmpty(
            roots,
            isCollapsible: e => !e.HasImages,
            mergeData: (parent, child) =>
            {
                var parentName = parent.CollapsedDisplayName
                                 ?? PathTreeBuilder.DisplayName(parent.Path);
                var childName = child.CollapsedDisplayName
                                ?? PathTreeBuilder.DisplayName(child.Path);

                return child with
                {
                    CollapsedDisplayName = parentName + " / " + childName
                };
            });

        treeView.UpdateData(roots);
    }

    private void UpdateSelection(string? absoluteDir, TreeView<DirEntry> treeView)
    {
        if (absoluteDir == null)
        {
            treeView.ClearPick();
            return;
        }

        foreach (var (relPath, absPath) in _directoryMap)
        {
            if (string.Equals(absPath, absoluteDir, StringComparison.Ordinal))
            {
                treeView.Locate(e => e.Path == relPath);
                return;
            }
        }
    }

    /// <summary>
    /// True if the section should mark itself non-present (no entries).
    /// </summary>
    public bool IsEmpty => _directoryMap.Count == 0;

    private static string FindCommonRoot(List<string> paths)
    {
        if (paths.Count == 0) return "";
        if (paths.Count == 1) return Path.GetDirectoryName(paths[0]) ?? paths[0];

        var first = paths[0];
        var commonLength = first.Length;

        for (var i = 1; i < paths.Count; i++)
        {
            var other = paths[i];
            commonLength = Math.Min(commonLength, other.Length);

            for (var j = 0; j < commonLength; j++)
            {
                if (first[j] != other[j])
                {
                    commonLength = j;
                    break;
                }
            }
        }

        var common = first[..commonLength];
        var lastSep = common.LastIndexOf(Path.DirectorySeparatorChar);
        return lastSep >= 0 ? common[..lastSep] : common;
    }
}