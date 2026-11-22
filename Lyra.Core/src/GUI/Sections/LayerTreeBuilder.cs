using Lyra.Imaging.Content;
using Lyra.UI.Components.Controls.TreeView;

namespace Lyra.GUI.Sections;

// ============================================================================
//  LayerTreeBuilder - reconstructs a layered file's group hierarchy from flat entries.
// ----------------------------------------------------------------------------
//  PSD stores layers as a flat list in bottom-to-top visual order, with
//  section-divider markers (the 'lsct' additional layer information) that
//  bracket groups:
//
//    GroupDivider     - marks the bottom of a group's contents
//    OpenGroup        - group header (visually expanded by default)
//    ClosedGroup      - group header (visually collapsed by default)
//    Layer            - regular layer
//
//  File order for  OuterGroup { leaf1, leaf2, InnerGroup { leaf3 } }  is:
//
//    GroupDivider (outer bottom)
//    leaf1
//    leaf2
//    GroupDivider (inner bottom)
//    leaf3
//    InnerGroup header
//    OuterGroup header
//
//  We walk forward with a stack of child lists: a GroupDivider pushes a
//  new list, a group header pops one and wraps it in a group node. File
//  order is bottom-up; we reverse at the end so the TreeView renders in
//  Photoshop-UI top-down order.
// ============================================================================
public static class LayerTreeBuilder
{
    /// <summary>
    /// Builds the group hierarchy from a flat record list.
    /// Empty input returns an empty forest. Malformed files (unbalanced
    /// dividers/headers) are tolerated: stray headers become normal nodes,
    /// unclosed groups are flattened into the enclosing scope.
    /// </summary>
    public static List<TreeNode<LayerEntry>> Build(IReadOnlyList<LayerEntry> records)
    {
        var roots = new List<TreeNode<LayerEntry>>();

        if (records.Count == 0)
            return roots;

        // stack[0] is the root frame; each push opens a pending group scope.
        var stack = new Stack<List<TreeNode<LayerEntry>>>();
        stack.Push(roots);

        foreach (var record in records)
        {
            switch (record.Section)
            {
                case LayerSection.GroupDivider:
                    stack.Push(new List<TreeNode<LayerEntry>>());
                    break;

                case LayerSection.OpenGroup:
                case LayerSection.ClosedGroup:
                    if (stack.Count <= 1)
                    {
                        // Stray header with no matching divider - degrade to a normal node.
                        stack.Peek().Add(new TreeNode<LayerEntry>(record));
                        break;
                    }

                    var groupChildren = stack.Pop();
                    var groupNode = new TreeNode<LayerEntry>(record)
                    {
                        IsExpanded = record.Section == LayerSection.OpenGroup
                    };
                    foreach (var child in groupChildren)
                        groupNode.AttachChild(child);

                    stack.Peek().Add(groupNode);
                    break;

                default:
                    stack.Peek().Add(new TreeNode<LayerEntry>(record));
                    break;
            }
        }

        // Unclosed groups: flatten accumulated children into the outer scope
        // so no data is lost on malformed input.
        while (stack.Count > 1)
        {
            var orphans = stack.Pop();
            var parent = stack.Peek();
            foreach (var child in orphans)
                parent.Add(child);
        }

        ReverseRecursive(roots);
        return roots;
    }

    private static void ReverseRecursive(List<TreeNode<LayerEntry>> nodes)
    {
        nodes.Reverse();
        foreach (var node in nodes)
            ReverseRecursive(node.Children);
    }
}