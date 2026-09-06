namespace Lyra.UI.Components.Controls.TreeView;

// ============================================================================
//  PathTreeBuilder - builds TreeNode forests from sorted paths
// ----------------------------------------------------------------------------
//  Input:  Distinct directory paths, ordered so that a directory comes
//          before anything inside it (any ordinal sort does this, since a
//          prefix always sorts before what extends it)
//  Output: List of root TreeNode representing the tree
//
//  Each node's Data is the full path (or item containing it),
//  not just the directory name. This allows Locate() and Remove()
//  to match on full paths. The row factory extracts the display
//  name from the last segment.
//
//  Example:
//    Input:  ["/photos", "/photos/vacation", "/photos/work", "/docs"]
//    Output: TreeNode "/photos"
//              ├─ TreeNode "/photos/vacation"
//              └─ TreeNode "/photos/work"
//            TreeNode "/docs"
// ============================================================================
public static class PathTreeBuilder
{
    public static List<TreeNode<string>> Build(IEnumerable<string> sortedPaths) => Build(sortedPaths, p => p);
    
    public static List<TreeNode<T>> Build<T>(IEnumerable<T> sortedItems, Func<T, string> pathSelector)
    {
        var roots = new List<TreeNode<T>>();
        
        var byPath = new Dictionary<string, TreeNode<T>>(StringComparer.Ordinal);

        foreach (var item in sortedItems)
        {
            var path = pathSelector(item).Trim('/');
            if (path.Length == 0)
                continue;

            TreeNode<T> node;

            if (NearestAncestor(byPath, path) is { } parent)
            {
                node = parent.AddChild(item);
            }
            else
            {
                node = new TreeNode<T>(item);
                roots.Add(node);
            }

            byPath[path] = node;
        }

        return roots;
    }
    
    private static TreeNode<T>? NearestAncestor<T>(Dictionary<string, TreeNode<T>> byPath, string path)
    {
        for (var cut = path.LastIndexOf('/'); cut > 0; cut = path.LastIndexOf('/', cut - 1))
            if (byPath.TryGetValue(path[..cut], out var ancestor))
                return ancestor;

        return null;
    }

    /// <summary>
    /// Extracts the display name (last path segment) from a
    /// full path stored in a TreeNode<string>.
    /// Useful in row factories for display.
    /// </summary>
    public static string DisplayName(string fullPath)
    {
        var lastSlash = fullPath.LastIndexOf('/');
        return lastSlash >= 0 ? fullPath[(lastSlash + 1)..] : fullPath;
    }

    /// <summary>
    /// Collapses chains of single-child nodes where isCollapsible returns true.
    /// mergeData combines parent + child data when collapsing (called iteratively
    /// for chains longer than 2).
    ///
    /// Example: a(empty) -> b(empty) -> c(images) becomes "a / b / c"(images)
    /// </summary>
    public static List<TreeNode<T>> CollapseEmpty<T>(List<TreeNode<T>> roots, Func<T, bool> isCollapsible, Func<T, T, T> mergeData)
    {
        return roots.Select(r => CollapseNode(r, isCollapsible, mergeData)).ToList();
    }

    private static TreeNode<T> CollapseNode<T>(TreeNode<T> node, Func<T, bool> isCollapsible, Func<T, T, T> mergeData)
    {
        // Recursively collapse children first.
        var collapsed = node.Children
            .Select(c => CollapseNode(c, isCollapsible, mergeData))
            .ToList();

        // Collapsible + exactly one child -> merge into child.
        if (isCollapsible(node.Data) && collapsed.Count == 1)
        {
            var child = collapsed[0];
            var merged = new TreeNode<T>(mergeData(node.Data, child.Data))
            {
                IsExpanded = child.IsExpanded
            };

            foreach (var grandchild in child.Children)
                merged.AttachChild(grandchild);

            return merged;
        }

        // Check if any child was restructured.
        var changed = false;
        for (var i = 0; i < node.Children.Count; i++)
        {
            if (!ReferenceEquals(node.Children[i], collapsed[i]))
            {
                changed = true;
                break;
            }
        }

        if (!changed)
            return node;

        // Rebuild with collapsed children.
        var rebuilt = new TreeNode<T>(node.Data) { IsExpanded = node.IsExpanded };
        foreach (var child in collapsed)
            rebuilt.AttachChild(child);

        return rebuilt;
    }
}