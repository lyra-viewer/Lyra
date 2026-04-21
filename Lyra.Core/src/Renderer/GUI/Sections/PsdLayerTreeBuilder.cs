using Lyra.Psd.Core.Decode.Layers;
using Lyra.UI.Components.Controls.TreeView;

namespace Lyra.Renderer.GUI.Sections;

// ============================================================================
//  PsdLayerTreeBuilder - reconstructs PSD group hierarchy from flat records.
// ----------------------------------------------------------------------------
//  PSD stores layers as a flat list in bottom-to-top visual order, with
//  section-divider markers (the 'lsct' additional layer information) that
//  bracket groups:
//
//    BoundingDivider  - marks the bottom of a group's contents
//    OpenGroup        - group header (visually expanded by default)
//    ClosedGroup      - group header (visually collapsed by default)
//    Normal           - regular layer
//
//  File order for  OuterGroup { leaf1, leaf2, InnerGroup { leaf3 } }  is:
//
//    BoundingDivider (outer bottom)
//    leaf1
//    leaf2
//    BoundingDivider (inner bottom)
//    leaf3
//    InnerGroup header
//    OuterGroup header
//
//  We walk forward with a stack of child lists: a BoundingDivider pushes a
//  new list, a group header pops one and wraps it in a group node. File
//  order is bottom-up; we reverse at the end so the TreeView renders in
//  Photoshop-UI top-down order.
// ============================================================================
public static class PsdLayerTreeBuilder
{
    /// <summary>
    /// Builds the group hierarchy from a flat record list.
    /// Empty input returns an empty forest. Malformed files (unbalanced
    /// dividers/headers) are tolerated: stray headers become normal nodes,
    /// unclosed groups are flattened into the enclosing scope.
    /// </summary>
    public static List<TreeNode<LayerRecord>> Build(IReadOnlyList<LayerRecord> records)
    {
        var roots = new List<TreeNode<LayerRecord>>();

        if (records.Count == 0)
            return roots;

        // stack[0] is the root frame; each push opens a pending group scope.
        var stack = new Stack<List<TreeNode<LayerRecord>>>();
        stack.Push(roots);

        foreach (var record in records)
        {
            switch (record.SectionType)
            {
                case LayerSectionType.BoundingDivider:
                    stack.Push(new List<TreeNode<LayerRecord>>());
                    break;

                case LayerSectionType.OpenGroup:
                case LayerSectionType.ClosedGroup:
                    if (stack.Count <= 1)
                    {
                        // Stray header with no matching divider - degrade to a normal node.
                        stack.Peek().Add(new TreeNode<LayerRecord>(record));
                        break;
                    }

                    var groupChildren = stack.Pop();
                    var groupNode = new TreeNode<LayerRecord>(record)
                    {
                        IsExpanded = record.SectionType == LayerSectionType.OpenGroup
                    };
                    foreach (var child in groupChildren)
                        groupNode.AttachChild(child);

                    stack.Peek().Add(groupNode);
                    break;

                default:
                    stack.Peek().Add(new TreeNode<LayerRecord>(record));
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

    private static void ReverseRecursive(List<TreeNode<LayerRecord>> nodes)
    {
        nodes.Reverse();
        foreach (var node in nodes)
            ReverseRecursive(node.Children);
    }
}