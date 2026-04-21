namespace Lyra.UI.Components.Controls.TreeView;

// ============================================================================
//  PathTreeBuilder - builds TreeNode forests from sorted paths
// ----------------------------------------------------------------------------
//  Input:  Sorted, distinct directory paths (e.g., from Lyra)
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
    /// Builds a tree from sorted, distinct directory paths.
    /// Uses '/' as the path separator. Leading/trailing slashes
    /// are trimmed.
    public static List<TreeNode<string>> Build(IEnumerable<string> sortedPaths)
        => Build(sortedPaths, p => p);

    /// Generic overload: builds a tree from sorted items with a
    /// path accessor. Each item's path is used for depth/parent
    /// calculation; the item itself is stored in the node.
    public static List<TreeNode<T>> Build<T>(IEnumerable<T> sortedItems, Func<T, string> pathSelector)
    {
        var roots = new List<TreeNode<T>>();

        // Stack tracks the current path from root to deepest node.
        var stack = new List<(int Depth, TreeNode<T> Node)>();

        foreach (var item in sortedItems)
        {
            var rawPath = pathSelector(item);
            var trimmed = rawPath.Trim('/');
            var segments = trimmed.Split('/', StringSplitOptions.RemoveEmptyEntries);

            if (segments.Length == 0)
                continue;

            var depth = segments.Length - 1;

            // Pop stack back to the parent depth.
            while (stack.Count > 0 && stack[^1].Depth >= depth)
                stack.RemoveAt(stack.Count - 1);

            // Store the item (whose path is the full normalized path).
            TreeNode<T> node;
            if (stack.Count == 0)
            {
                node = new TreeNode<T>(item);
                roots.Add(node);
            }
            else
            {
                var parent = stack[^1].Node;
                node = parent.AddChild(item);
            }

            stack.Add((depth, node));
        }

        return roots;
    }

    /// Extracts the display name (last path segment) from a
    /// full path stored in a TreeNode<string>.
    /// Useful in row factories for display.
    public static string DisplayName(string fullPath)
    {
        var lastSlash = fullPath.LastIndexOf('/');
        return lastSlash >= 0 ? fullPath[(lastSlash + 1)..] : fullPath;
    }

    /// Collapses chains of single-child nodes where isCollapsible returns true.
    /// mergeData combines parent + child data when collapsing (called iteratively
    /// for chains longer than 2).
    ///
    /// Example: a(empty) -> b(empty) -> c(images) becomes "a / b / c"(images)
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