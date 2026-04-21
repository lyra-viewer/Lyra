namespace Lyra.UI.Components.Controls.TreeView;

// ============================================================================
//  TreeNode<T> - generic tree data model
// ----------------------------------------------------------------------------
//  Plain data structure with no UI concerns. Holds the user's data,
//  child relationships, and expansion state.
//
//  TreeView<T> consumes these to build a flattened list of visible
//  rows for rendering. The node tree can be built from any source
//  (flat path list, PSD group hierarchy, etc.) via external builders.
// ============================================================================
public class TreeNode<T>
{
    public T Data { get; }
    public List<TreeNode<T>> Children { get; } = [];
    public TreeNode<T>? Parent { get; private set; }

    /// Controls whether children are visible in the tree.
    /// Leaf nodes ignore this.
    public bool IsExpanded { get; set; }

    /// Depth in the tree (root = 0). Set when added to a parent.
    public int Depth { get; private set; }

    public bool IsLeaf => Children.Count == 0;

    public TreeNode(T data)
    {
        Data = data;
    }

    public TreeNode<T> AddChild(T data)
    {
        var child = new TreeNode<T>(data)
        {
            Parent = this,
            Depth = Depth + 1
        };
        Children.Add(child);
        return child;
    }

    public void AddChildren(IEnumerable<T> items)
    {
        foreach (var item in items)
            AddChild(item);
    }

    // --------------------------------------------------------
    //  Traversal
    // --------------------------------------------------------

    /// Walks up to the root, returning ancestors bottom-up
    /// (immediate parent first, root last).
    public IEnumerable<TreeNode<T>> Ancestors()
    {
        var current = Parent;
        while (current != null)
        {
            yield return current;
            current = current.Parent;
        }
    }

    /// Expands this node and all ancestors so it becomes
    /// visible in the tree.
    public void ExpandToRoot()
    {
        foreach (var ancestor in Ancestors())
            ancestor.IsExpanded = true;
    }

    /// Depth-first search for a node matching the predicate.
    public TreeNode<T>? Find(Func<T, bool> predicate)
    {
        if (predicate(Data))
            return this;

        foreach (var child in Children)
        {
            var found = child.Find(predicate);
            if (found != null)
                return found;
        }

        return null;
    }

    /// Returns all visible nodes in depth-first order
    /// (expanded children only). Used by TreeView to build
    /// the flat row list.
    public IEnumerable<TreeNode<T>> FlattenVisible()
    {
        yield return this;

        if (!IsExpanded)
            yield break;

        foreach (var child in Children)
        {
            foreach (var node in child.FlattenVisible())
                yield return node;
        }
    }
    
    /// Attaches an existing node (and its full subtree) as a child.
    /// Used by post-build tree operations (e.g., CollapseEmpty).
    public TreeNode<T> AttachChild(TreeNode<T> child)
    {
        child.Parent = this;
        Children.Add(child);
        child.RecomputeDepth();
        return child;
    }

    private void RecomputeDepth()
    {
        Depth = Parent?.Depth + 1 ?? 0;
        foreach (var c in Children)
            c.RecomputeDepth();
    }
}